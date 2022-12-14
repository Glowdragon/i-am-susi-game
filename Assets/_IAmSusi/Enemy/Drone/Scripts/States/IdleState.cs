using System.Linq;
using DigitalRuby.Tween;
using Game.Utility;
using UnityEngine;

namespace Game.Drone
{
    public class IdleState: DroneState
    {
        private Waypoint currentWaypoint;
        
        public DroneStateId GetId()
        {
            return DroneStateId.Idle;
        }

        public void Enter(DroneAgent agent)
        {
            agent.Drone.Model.SpotAvatarTrigger.gameObject.SetActive(true);
        }

        public void Update(DroneAgent agent)
        {
            var myPos = agent.Drone.Model.transform.position;
            var path = agent.Drone.IdlePath;
            
            if (path == null)
            {
                Debug.LogError("No idle path set", agent.gameObject);
                return;
            }
            
            if (this.currentWaypoint == null)
            {
                this.currentWaypoint = path.Waypoints.OrderBy(w => w.Position.FlatDistanceTo(myPos)).First();
            }

            var distanceToWaypoint = myPos.FlatDistanceTo(this.currentWaypoint.Position);

            if (distanceToWaypoint < 30)
            {
                this.currentWaypoint = path.Waypoints[(path.Waypoints.IndexOf(this.currentWaypoint) + 1) % path.Waypoints.Count];
            }
            
            var moveTarget = this.currentWaypoint.Position;
            agent.Drone.Controller.SetMoveTarget(moveTarget);
        }

        public void Exit(DroneAgent agent)
        {
            agent.Drone.Model.SpotAvatarTrigger.gameObject.SetActive(false);
        }

        public void Dispose() {
        }
    }
}