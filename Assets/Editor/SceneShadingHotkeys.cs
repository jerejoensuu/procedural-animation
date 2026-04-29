using UnityEditor;

public static class SceneShadingHotkeys
{
    private static void ApplyAll(DrawCameraMode mode, bool? lighting = null)
    {
        var camMode = SceneView.GetBuiltinCameraMode(mode);

        // Apply to all open Scene views
        foreach (var svObj in SceneView.sceneViews)
        {
            if (svObj is SceneView sceneView)
            {
                sceneView.cameraMode = camMode;
                if (lighting.HasValue) sceneView.sceneLighting = lighting.Value;
                sceneView.Repaint();
            }
        }

        // Also apply to last active, in case no Scene view was iterated above
        var last = SceneView.lastActiveSceneView;
        if (last != null)
        {
            last.cameraMode = camMode;
            if (lighting.HasValue) last.sceneLighting = lighting.Value;
            last.Repaint();
        }
    }

    [MenuItem("Tools/Shading/Shaded _F4")]
    private static void Shaded() => ApplyAll(DrawCameraMode.Textured, true);

    [MenuItem("Tools/Shading/Unlit _F3")]
    private static void Unlit() => ApplyAll(DrawCameraMode.Textured, false);

    [MenuItem("Tools/Shading/Shaded Wireframe _F2")]
    private static void ShadedWire() => ApplyAll(DrawCameraMode.TexturedWire, true);

    [MenuItem("Tools/Shading/Wireframe _F1")]
    private static void Wire() => ApplyAll(DrawCameraMode.Wireframe, true);
}