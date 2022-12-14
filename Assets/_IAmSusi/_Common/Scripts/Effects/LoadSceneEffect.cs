using MyBox;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

namespace Game.Effects {
    [CreateAssetMenu]
    sealed class LoadSceneEffect : EffectBase {
        [SerializeField]
        SceneReference scene = new();
        [SerializeField]
        LoadSceneMode loadSceneMode = LoadSceneMode.Single;

        protected override void InvokeNow(GameObject context) {
            Assert.IsTrue(scene.IsAssigned, $"Scene has not been assigned to asset {this}!");
            scene.LoadScene(loadSceneMode);
        }
    }
}
