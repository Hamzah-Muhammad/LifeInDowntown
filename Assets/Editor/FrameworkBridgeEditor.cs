using UnityEditor;

namespace LifeInDowntown.EditorTools
{
    // edit mode (tests, renders, the development range) has no RuntimeInitializeOnLoad: plug the game into
    // MinniGameFramework as soon as the editor loads the scripts (docs/DESIGN.md 3S)
    static class FrameworkBridgeEditor
    {
        [InitializeOnLoadMethod]
        static void Install() => LifeInDowntown.Game.FrameworkBridge.Install();
    }
}
