using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace IVRC.EditorTools
{
    /// <summary>
    /// Copies the theatre's seat positions onto the audience light points.
    ///
    /// The placeholder crowd's 55 transforms were the obvious placement
    /// data, but measuring them found they do not sit on the theatre's
    /// seats: their front row lands in the aisle and their back row
    /// overshoots the last row of seats by more than a metre. The seats
    /// themselves are the only honest source, so the capture reads those.
    ///
    /// Captured once and serialized, so nothing at runtime has to know how
    /// the theatre asset is organised or what its objects are called.
    /// </summary>
    internal static class AudienceSeatCaptureMenu
    {
        private const string MenuPath =
            "IVRC/Capture Audience Seats";

        // The theatre asset groups its seating under two parents, one per
        // seat model. Names come from the asset and are Chinese for
        // "seat 1" and "seat 2".
        private static readonly string[] SeatGroupNames =
        {
            "座椅1",
            "座椅2"
        };

        // The asset's "seats" are benches 1.9 m and 2.7 m wide, not single
        // chairs. Taking one point per bench put the whole crowd in three
        // columns, which is what the benches are, not what a crowd is. Each
        // bench is divided at roughly the width of one person instead.
        private const float SeatPitchMetres = 0.62f;

        // The asset models one row as two or three overlapping meshes at the
        // same footprint and slightly different heights, all named the same
        // and parented together. Treating each as its own bench seated the
        // same person two or three times, stacked vertically. Anything
        // closer than half a seat's width in plan is the same place.
        private const float SamePlaceMetres = SeatPitchMetres * 0.5f;

        [MenuItem(MenuPath, priority = 20)]
        private static void Capture()
        {
            var target = Object
                .FindObjectsByType<AudienceLightPoints>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault();
            if (target == null)
            {
                EditorUtility.DisplayDialog(
                    "Capture Audience Seats",
                    "No AudienceLightPoints in the open scene.",
                    "OK");
                return;
            }

            var theatre = FindTheatre();
            if (theatre == null)
            {
                EditorUtility.DisplayDialog(
                    "Capture Audience Seats",
                    "Could not find Phase_Posing/Theater in the " +
                    "open scene.",
                    "OK");
                return;
            }

            var seats = Deduplicate(CollectSeats(theatre));
            if (seats.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Capture Audience Seats",
                    "Found the theatre but no seats under " +
                    string.Join(" or ", SeatGroupNames) +
                    ". The asset may have been reorganised.",
                    "OK");
                return;
            }

            // Front to back, so the captured order reads the way the room
            // does when someone opens the array to check it.
            var positions = seats
                .Select(seat =>
                    target.transform.InverseTransformPoint(
                        seat))
                .OrderBy(position => position.z)
                .ThenBy(position => position.x)
                .ToArray();

            var serialized = new SerializedObject(target);
            var array = serialized
                .FindProperty("_seatPositions");
            array.arraySize = positions.Length;
            for (var index = 0;
                 index < positions.Length;
                 index++)
            {
                array
                    .GetArrayElementAtIndex(index)
                    .vector3Value = positions[index];
            }

            serialized.ApplyModifiedProperties();
            target.Seed();

            Debug.Log(
                $"[Audience] Captured {positions.Length} seats " +
                $"onto {target.name}. Front row z=" +
                $"{positions.First().z:F2}, back row z=" +
                $"{positions.Last().z:F2}. The scene now has an " +
                "unsaved change.");
        }

        private static Transform FindTheatre()
        {
            var scene = UnityEngine.SceneManagement
                .SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "Phase_Posing")
                {
                    continue;
                }

                return root.transform.Find("Theater");
            }

            return null;
        }

        /// <summary>
        /// Keeps one person per place on the floor. Tallest first, so the
        /// survivor is the seat surface rather than the frame under it.
        /// </summary>
        private static List<Vector3> Deduplicate(
            List<Vector3> candidates)
        {
            var kept = new List<Vector3>();
            foreach (var candidate in candidates
                         .OrderByDescending(seat => seat.y))
            {
                var occupied = false;
                foreach (var taken in kept)
                {
                    var dx = taken.x - candidate.x;
                    var dz = taken.z - candidate.z;
                    if (dx * dx + dz * dz <
                        SamePlaceMetres * SamePlaceMetres)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                {
                    kept.Add(candidate);
                }
            }

            return kept;
        }

        private static List<Vector3> CollectSeats(
            Transform theatre)
        {
            var seats = new List<Vector3>();
            foreach (var renderer in theatre
                         .GetComponentsInChildren<MeshRenderer>(
                             true))
            {
                var parent = renderer.transform.parent;
                if (parent == null)
                {
                    continue;
                }

                if (!SeatGroupNames.Contains(parent.name))
                {
                    continue;
                }

                AddSeatsAlongBench(renderer, seats);
            }

            return seats;
        }

        private static void AddSeatsAlongBench(
            MeshRenderer renderer,
            List<Vector3> seats)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null)
            {
                return;
            }

            var local = renderer.localBounds;
            var transform = renderer.transform;
            var scale = transform.lossyScale;

            // Whichever local axis the bench is long along. Reading it from
            // the mesh rather than assuming X keeps this working if a row
            // is modelled turned.
            var axis = 0;
            var widest = Mathf.Abs(local.size.x * scale.x);
            var alongY = Mathf.Abs(local.size.y * scale.y);
            var alongZ = Mathf.Abs(local.size.z * scale.z);
            if (alongY > widest) { axis = 1; widest = alongY; }
            if (alongZ > widest) { axis = 2; widest = alongZ; }

            var count = Mathf.Max(
                1,
                Mathf.RoundToInt(widest / SeatPitchMetres));

            // The benches are arcs, not straight runs, and the widest of
            // them bows 0.74 m away from the line between its ends.
            // Spacing along the bounding box would seat three quarters of
            // a metre of that row in mid air, so the mesh itself is
            // followed: vertices are binned along the long axis and each
            // bin's mean is one place to sit.
            var vertices = mesh.vertices;
            var sums = new Vector3[count];
            var counts = new int[count];
            var min = local.min[axis];
            var max = local.max[axis];

            foreach (var vertex in vertices)
            {
                var along = Mathf.InverseLerp(
                    min,
                    max,
                    vertex[axis]);
                var bin = Mathf.Clamp(
                    Mathf.FloorToInt(along * count),
                    0,
                    count - 1);
                sums[bin] += vertex;
                counts[bin]++;
            }

            // Sit the people on top of the bench rather than inside it, so
            // one height reads the same on both bench models even though
            // they are different depths.
            //
            // Taken from the mesh rather than renderer.bounds because the
            // posing phase is switched off while the scene is being
            // authored, and an inactive renderer's world bounds are not
            // kept up to date. Capturing with the phase off and with it on
            // otherwise gave heights 0.38 m apart.
            var seatTop = float.MinValue;
            foreach (var vertex in vertices)
            {
                var y = transform.TransformPoint(vertex).y;
                if (y > seatTop)
                {
                    seatTop = y;
                }
            }

            for (var index = 0; index < count; index++)
            {
                if (counts[index] == 0)
                {
                    continue;
                }

                var world = transform.TransformPoint(
                    sums[index] / counts[index]);
                world.y = seatTop;
                seats.Add(world);
            }
        }
    }
}
