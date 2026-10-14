package com.instantmuscle.network;

import android.app.Activity;
import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.net.wifi.WifiManager;
import android.net.wifi.WifiNetworkSpecifier;
import android.os.Build;
import android.os.SystemClock;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * Owns the Quest-only local Wi-Fi request and the UDP socket bound to it.
 *
 * The ESP32 firmware is deliberately not changed by this bridge. It still
 * expects one-byte 'T' (relief) / 'F' (build) datagrams at 192.168.4.1:5000.
 * The returned Android Network is used only for this DatagramSocket; the
 * Unity process is never rebound to the ESP32 network.
 */
public final class QuestLocalWifiUdpBridge {
    public static final int STATE_IDLE = 0;
    public static final int STATE_REQUESTING = 1;
    public static final int STATE_ROUTE_READY = 2;
    public static final int STATE_UNAVAILABLE = 3;
    public static final int STATE_LOST = 4;
    public static final int STATE_FAULTED = 5;
    public static final int STATE_UNSUPPORTED = 6;

    private static final int RELIEF = (int) 'T';
    private static final int BUILD = (int) 'F';
    private static final int RELIEF_BURST_COUNT = 3;
    private static final long UNITY_COMMAND_WATCHDOG_MS = 250L;
    private static final long WATCHDOG_CHECK_INTERVAL_MS = 50L;
    private static final long WATCHDOG_RELIEF_INTERVAL_MS = 100L;
    private static final Object LOCK = new Object();

    private static ConnectivityManager connectivityManager;
    private static ConnectivityManager.NetworkCallback networkCallback;
    private static boolean requestRegistered;
    private static Network activeNetwork;
    private static DatagramSocket socket;
    private static InetAddress destinationAddress;
    private static int destinationPort;
    private static String destinationHost;
    private static int state = STATE_IDLE;
    private static String lastError = "";
    private static boolean buildAllowed;
    private static boolean watchdogReliefLatched;
    private static long lastUnityCommandAtMs;
    private static long lastWatchdogReliefAtMs;
    private static ScheduledExecutorService watchdogExecutor;

    private QuestLocalWifiUdpBridge() {
    }

    /**
     * Requests the ESP32 SoftAP as a local-only Wi-Fi network.
     * The first request can display Android's system consent dialog.
     */
    public static void start(
            Activity activity,
            String ssid,
            String passphrase,
            String host,
            int port) {
        synchronized (LOCK) {
            stopLocked();
            buildAllowed = false;

            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) {
                setFailureLocked(
                        STATE_UNSUPPORTED,
                        "WifiNetworkSpecifier requires Android 10 or newer.");
                return;
            }

            if (activity == null) {
                setFailureLocked(STATE_FAULTED, "Unity Activity is unavailable.");
                return;
            }

            if (ssid == null || ssid.length() == 0) {
                setFailureLocked(STATE_FAULTED, "ESP32 Wi-Fi SSID is empty.");
                return;
            }

            if (host == null || host.length() == 0) {
                setFailureLocked(STATE_FAULTED, "ESP32 host address is empty.");
                return;
            }

            if (port <= 0 || port > 65535) {
                setFailureLocked(STATE_FAULTED, "ESP32 UDP port is invalid.");
                return;
            }

            try {
                Context context = activity.getApplicationContext();
                connectivityManager = (ConnectivityManager) context.getSystemService(
                        Context.CONNECTIVITY_SERVICE);
                if (connectivityManager == null) {
                    setFailureLocked(
                            STATE_FAULTED,
                            "ConnectivityManager is unavailable.");
                    return;
                }

                WifiNetworkSpecifier.Builder specifierBuilder =
                        new WifiNetworkSpecifier.Builder().setSsid(ssid);
                if (passphrase != null && passphrase.length() > 0) {
                    specifierBuilder.setWpa2Passphrase(passphrase);
                }

                WifiNetworkSpecifier specifier = specifierBuilder.build();
                NetworkRequest request = new NetworkRequest.Builder()
                        .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
                        .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                        .setNetworkSpecifier(specifier)
                        .build();

                destinationHost = host;
                destinationPort = port;
                state = STATE_REQUESTING;
                lastError = "";

                final ConnectivityManager.NetworkCallback callback =
                        new ConnectivityManager.NetworkCallback() {
                            @Override
                            public void onAvailable(Network network) {
                                synchronized (LOCK) {
                                    if (networkCallback != this) {
                                        return;
                                    }
                                    openBoundSocketLocked(network);
                                }
                            }

                            @Override
                            public void onLost(Network network) {
                                synchronized (LOCK) {
                                    if (networkCallback != this ||
                                            activeNetwork == null ||
                                            !activeNetwork.equals(network)) {
                                        return;
                                    }

                                    // A lost route must never preserve authority
                                    // to build. A later onAvailable starts with T.
                                    buildAllowed = false;
                                    watchdogReliefLatched = true;
                                    closeSocketLocked();
                                    state = STATE_LOST;
                                    lastError = "ESP32 Wi-Fi route was lost.";
                                }
                            }

                            @Override
                            public void onUnavailable() {
                                synchronized (LOCK) {
                                    if (networkCallback != this) {
                                        return;
                                    }

                                    buildAllowed = false;
                                    watchdogReliefLatched = true;
                                    closeSocketLocked();
                                    requestRegistered = false;
                                    stopWatchdogLocked();
                                    state = STATE_UNAVAILABLE;
                                    lastError =
                                            "ESP32 Wi-Fi request was denied or unavailable.";
                                }
                            }
                        };

                networkCallback = callback;
                requestRegistered = true;
                connectivityManager.requestNetwork(request, callback);
                startWatchdogLocked();
            } catch (SecurityException exception) {
                requestRegistered = false;
                stopWatchdogLocked();
                setFailureLocked(
                        STATE_FAULTED,
                        "Missing Android network permission: " +
                                safeMessage(exception));
            } catch (RuntimeException exception) {
                requestRegistered = false;
                stopWatchdogLocked();
                setFailureLocked(
                        STATE_FAULTED,
                        "Could not request ESP32 Wi-Fi: " +
                                safeMessage(exception));
            }
        }
    }

    /**
     * Changes the independent hardware-safety gate. Turning it off emits a
     * best-effort T immediately. It never enables itself automatically.
     */
    public static void setBuildAllowed(boolean allowed) {
        synchronized (LOCK) {
            if (!allowed) {
                boolean wasAllowed = buildAllowed;
                buildAllowed = false;
                if (wasAllowed) {
                    sendReliefBestEffortLocked();
                }
                return;
            }

            buildAllowed = true;
        }
    }

    /**
     * Sends one existing-firmware command. Unsafe or unknown commands are
     * replaced with T and reported as rejected.
     */
    public static boolean send(int command) {
        synchronized (LOCK) {
            if (state != STATE_ROUTE_READY || socket == null) {
                return false;
            }

            lastUnityCommandAtMs = SystemClock.elapsedRealtime();
            if (command == RELIEF) {
                // A fresh T from Unity is required to clear a watchdog
                // latch. Resuming a stale stream of F cannot clear it.
                watchdogReliefLatched = false;
            }

            if (command == BUILD &&
                    (!buildAllowed || watchdogReliefLatched)) {
                sendReliefBestEffortLocked();
                return false;
            }

            if (command != RELIEF && command != BUILD) {
                sendReliefBestEffortLocked();
                return false;
            }

            try {
                sendRawLocked(command);
                return true;
            } catch (IOException exception) {
                buildAllowed = false;
                watchdogReliefLatched = true;
                if (tryReopenReliefOnActiveNetworkLocked()) {
                    // The command that failed is never retried. A recovered
                    // socket starts with T and still reports rejection so
                    // Unity must disarm/latch before any future F.
                    return false;
                }
                setFailureLocked(
                        STATE_FAULTED,
                        "UDP send failed: " + safeMessage(exception));
                return false;
            }
        }
    }

    /** Sends relief best-effort, releases the request, and closes the socket. */
    public static void stop() {
        synchronized (LOCK) {
            stopLocked();
        }
    }

    public static int getState() {
        synchronized (LOCK) {
            return state;
        }
    }

    public static String getLastError() {
        synchronized (LOCK) {
            return lastError;
        }
    }

    /**
     * Diagnostic only. A false result does not change the transport state;
     * the actual request result remains authoritative.
     */
    public static boolean isConcurrentLocalOnlySupported(Activity activity) {
        if (activity == null || Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
            return false;
        }

        try {
            WifiManager wifiManager = (WifiManager) activity
                    .getApplicationContext()
                    .getSystemService(Context.WIFI_SERVICE);
            return wifiManager != null &&
                    wifiManager.isStaConcurrencyForLocalOnlyConnectionsSupported();
        } catch (RuntimeException exception) {
            return false;
        }
    }

    private static void openBoundSocketLocked(Network network) {
        closeSocketLocked();
        buildAllowed = false;
        watchdogReliefLatched = true;

        DatagramSocket candidate = null;
        try {
            candidate = new DatagramSocket();
            // Per-socket binding keeps every unrelated Unity/Meta connection
            // on the normal default network.
            network.bindSocket(candidate);
            InetAddress address = InetAddress.getByName(destinationHost);

            socket = candidate;
            candidate = null;
            destinationAddress = address;
            activeNetwork = network;

            // Every new route begins released. F remains blocked until Unity
            // explicitly reapplies the independent hardware-safety gate.
            sendRawLocked(RELIEF);
            lastUnityCommandAtMs = SystemClock.elapsedRealtime();
            lastWatchdogReliefAtMs = lastUnityCommandAtMs;
            startWatchdogLocked();
            state = STATE_ROUTE_READY;
            lastError = "";
        } catch (IOException | RuntimeException exception) {
            if (candidate != null) {
                candidate.close();
            }
            closeSocketLocked();
            stopWatchdogLocked();
            setFailureLocked(
                    STATE_FAULTED,
                    "Could not bind UDP socket to ESP32 Wi-Fi: " +
                            safeMessage(exception));
        }
    }

    private static void sendRawLocked(int command) throws IOException {
        if (socket == null || destinationAddress == null) {
            throw new IOException("UDP route is not ready.");
        }

        byte[] payload = new byte[] { (byte) command };
        DatagramPacket packet = new DatagramPacket(
                payload,
                payload.length,
                destinationAddress,
                destinationPort);
        socket.send(packet);
    }

    private static void sendReliefBestEffortLocked() {
        if (state != STATE_ROUTE_READY || socket == null) {
            return;
        }

        try {
            sendRawLocked(RELIEF);
        } catch (IOException exception) {
            buildAllowed = false;
            watchdogReliefLatched = true;
            if (tryReopenReliefOnActiveNetworkLocked()) {
                return;
            }
            setFailureLocked(
                    STATE_FAULTED,
                    "UDP relief send failed: " + safeMessage(exception));
        }
    }

    private static void sendReliefBurstLocked() {
        if (state != STATE_ROUTE_READY || socket == null) {
            return;
        }

        for (int index = 0; index < RELIEF_BURST_COUNT; index++) {
            try {
                sendRawLocked(RELIEF);
            } catch (IOException exception) {
                lastError =
                        "UDP relief burst failed: " + safeMessage(exception);
                break;
            }
        }
    }

    private static void stopLocked() {
        buildAllowed = false;
        watchdogReliefLatched = true;
        sendReliefBurstLocked();
        closeSocketLocked();
        stopWatchdogLocked();

        if (requestRegistered &&
                connectivityManager != null &&
                networkCallback != null) {
            try {
                connectivityManager.unregisterNetworkCallback(networkCallback);
            } catch (RuntimeException ignored) {
                // onUnavailable can release a request before Unity stops it.
            }
        }

        requestRegistered = false;
        networkCallback = null;
        connectivityManager = null;
        destinationHost = null;
        destinationPort = 0;
        state = STATE_IDLE;
        lastError = "";
    }

    private static void startWatchdogLocked() {
        if (watchdogExecutor != null && !watchdogExecutor.isShutdown()) {
            return;
        }

        watchdogExecutor = Executors.newSingleThreadScheduledExecutor();
        watchdogExecutor.scheduleAtFixedRate(
                new Runnable() {
                    @Override
                    public void run() {
                        runWatchdogCheck();
                    }
                },
                WATCHDOG_CHECK_INTERVAL_MS,
                WATCHDOG_CHECK_INTERVAL_MS,
                TimeUnit.MILLISECONDS);
    }

    private static void stopWatchdogLocked() {
        if (watchdogExecutor == null) {
            return;
        }

        watchdogExecutor.shutdownNow();
        watchdogExecutor = null;
    }

    private static void runWatchdogCheck() {
        synchronized (LOCK) {
            if (state != STATE_ROUTE_READY || socket == null) {
                return;
            }

            long now = SystemClock.elapsedRealtime();
            if (!watchdogReliefLatched &&
                    now - lastUnityCommandAtMs >=
                            UNITY_COMMAND_WATCHDOG_MS) {
                buildAllowed = false;
                watchdogReliefLatched = true;
            }

            if (!watchdogReliefLatched ||
                    now - lastWatchdogReliefAtMs <
                            WATCHDOG_RELIEF_INTERVAL_MS) {
                return;
            }

            try {
                // Java never repeats F. During a Unity stall it can only
                // repeat T, and remains latched until Unity supplies T.
                sendRawLocked(RELIEF);
                lastWatchdogReliefAtMs = now;
            } catch (IOException exception) {
                buildAllowed = false;
                watchdogReliefLatched = true;
                if (tryReopenReliefOnActiveNetworkLocked()) {
                    return;
                }
                setFailureLocked(
                        STATE_FAULTED,
                        "UDP watchdog relief failed: " +
                                safeMessage(exception));
                stopWatchdogLocked();
            }
        }
    }

    /**
     * One immediate relief-only socket recovery on the Network Android still
     * reports available. This shortens a transient socket failure without
     * preserving F authority. A later C# backoff restarts the complete
     * NetworkRequest if this cannot reopen the route.
     */
    private static boolean tryReopenReliefOnActiveNetworkLocked() {
        Network network = activeNetwork;
        if (network == null) {
            closeSocketLocked();
            stopWatchdogLocked();
            return false;
        }

        openBoundSocketLocked(network);
        return state == STATE_ROUTE_READY && socket != null;
    }

    private static void closeSocketLocked() {
        if (socket != null) {
            socket.close();
        }
        socket = null;
        destinationAddress = null;
        activeNetwork = null;
    }

    private static void setFailureLocked(int failureState, String message) {
        state = failureState;
        lastError = message == null ? "Unknown network error." : message;
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null ? throwable.getClass().getSimpleName() : message;
    }
}
