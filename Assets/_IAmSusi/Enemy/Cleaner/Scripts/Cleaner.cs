using UnityEngine;

namespace Game.Cleaner
{
    public class Cleaner : MonoBehaviour
    {
        [SerializeField]
        private CleanerController controller;

        [SerializeField]
        private CleanerModel model;

        [SerializeField]
        private Path cleaningPath;

        public CleanerController Controller => this.controller;

        public CleanerModel Model => this.model;

        public Path CleaningPath => this.cleaningPath;
    }
}