using System;
using UnityEngine;

namespace Game.Units.Mech.Art
{
    /// <summary>
    /// Read-only view of a mech_frame@v1 JSON (research/R-005 §1.5; copy in Assets/Game/Config/Mech/).
    /// Only the fields the art side needs: segment ids, parents, joint positions, lengths, height.
    /// Frame: x right, y up, z forward (Unity world), metres; the mech's left is -x.
    /// </summary>
    [Serializable]
    public sealed class MechFrameConfig
    {
        public const string SchemaV1 = "mech_frame@v1";

        public string schema;
        public string id;
        public float total_height_m;
        public Segment[] segments;
        public Joint[] joints;

        [Serializable]
        public sealed class Segment
        {
            public string id;
            public string kind;
            public string side;
            public string parent;
            public float[] proximal_joint;
            public float[] distal_end;
            public float length_m;

            public Vector3 ProximalJoint => new Vector3(proximal_joint[0], proximal_joint[1], proximal_joint[2]);
            public bool IsRoot => string.IsNullOrEmpty(parent);
        }

        [Serializable]
        public sealed class Joint
        {
            public string id;
            public string parent;
            public string child;
            public float[] position;

            public Vector3 Position => new Vector3(position[0], position[1], position[2]);
        }

        /// <summary>Parses the JSON and refuses any schema other than mech_frame@v1.</summary>
        public static MechFrameConfig Parse(string json)
        {
            var cfg = JsonUtility.FromJson<MechFrameConfig>(json);
            if (cfg == null || cfg.schema != SchemaV1)
                throw new FormatException($"Expected schema {SchemaV1}, got '{cfg?.schema}'.");
            return cfg;
        }

        public Segment FindSegment(string segmentId)
        {
            foreach (var s in segments)
                if (s.id == segmentId) return s;
            return null;
        }
    }
}
