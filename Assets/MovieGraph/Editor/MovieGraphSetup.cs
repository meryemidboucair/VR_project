using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MovieGraph.EditorTools
{
    public static class MovieGraphSetup
    {
        [MenuItem("Tools/Movie Graph/Installer dans la scène")]
        public static void Setup()
        {
            // 1. Désactive l'ancien exemple (animaux) et les nœuds de test
            foreach (var b in Object.FindObjectsByType<AnimalGraphBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(b.gameObject, "Disable animal graph");
                b.gameObject.SetActive(false);
            }
            foreach (var name in new[] { "Node1", "Node2" })
            {
                var go = GameObject.Find(name);
                if (go != null) { Undo.RecordObject(go, "Disable test node"); go.SetActive(false); }
            }

            // 2. Graphe
            var manager = Object.FindFirstObjectByType<MovieGraphManager>();
            if (manager == null)
            {
                var root = new GameObject("MovieGraph");
                Undo.RegisterCreatedObjectUndo(root, "Create MovieGraph");
                root.transform.position = new Vector3(0f, 1.3f, 1.2f);
                manager = root.AddComponent<MovieGraphManager>();
            }

            // 3. Pointeur (souris + manette droite)
            var pointer = Object.FindFirstObjectByType<MovieGraphPointer>();
            if (pointer == null)
            {
                pointer = manager.gameObject.AddComponent<MovieGraphPointer>();
            }
            pointer.graph = manager;
            var right = GameObject.Find("RightControllerAnchor");
            if (right != null) pointer.rayOrigin = right.transform;

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = manager.gameObject;

            Debug.Log("[MovieGraph] Installé. Colle ta clé TMDB dans MovieGraphManager > Tmdb Api Key, puis lance Play." +
                      (right == null ? "\n(RightControllerAnchor introuvable : seule la souris fonctionnera)" : ""));
        }
    }
}
