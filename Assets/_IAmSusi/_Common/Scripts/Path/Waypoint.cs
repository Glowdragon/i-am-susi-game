using UnityEngine;

namespace Game {
    public class Waypoint {
        Vector3 position;

        public Waypoint(Vector3 position) {
            this.position = position;
        }

        public Vector3 Position => position;
    }
}