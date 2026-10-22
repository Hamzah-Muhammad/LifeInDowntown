using UnityEditor;
using UnityEditor.SceneManagement;

namespace LifeInDowntown.EditorTools
{
    // Opens the city scene. Used as -executeMethod when launching the editor from the command line.
    public static class OpenDowntown
    {
        [MenuItem("Life In Downtown/Open Downtown Scene")]
        public static void Open() =>
            EditorSceneManager.OpenScene("Assets/Scenes/Downtown_YongeDundas.unity", OpenSceneMode.Single);
    }
}
