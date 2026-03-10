using UnityEditor;
using UnityEngine;

public static class ConvertMaterialsToURP
{
    [MenuItem("Tools/Convert ThreeBox Materials to URP")]
    public static void Convert()
    {
        string[] matPaths = new string[]
        {
            "Assets/ThreeBox/Match3D Object Pack - Fruits and Vegetables/Resources/Materials/Fruits/Fruits_mtl.mat",
            "Assets/ThreeBox/Match3D Object Pack - Fruits and Vegetables/Resources/Materials/Vegetables/Vegetables_mtl.mat",
        };

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("[ConvertMaterialsToURP] URP/Lit shader not found!");
            return;
        }

        foreach (var path in matPaths)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogWarning($"[ConvertMaterialsToURP] Material not found: {path}");
                continue;
            }

            // Cache textures before shader change
            Texture albedo = mat.GetTexture("_MainTex");
            Texture normal = mat.GetTexture("_BumpMap");
            Texture occlusion = mat.GetTexture("_OcclusionMap");
            Color color = mat.GetColor("_Color");
            float smoothness = mat.GetFloat("_Glossiness");
            float metallic = mat.GetFloat("_Metallic");
            float normalScale = mat.GetFloat("_BumpScale");

            // Switch shader
            mat.shader = urpLit;

            // Re-assign properties with URP names
            mat.SetColor("_BaseColor", color);
            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            if (normal != null) mat.SetTexture("_BumpMap", normal);
            if (occlusion != null) mat.SetTexture("_OcclusionMap", occlusion);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_BumpScale", normalScale);

            EditorUtility.SetDirty(mat);
            Debug.Log($"<color=green>[ConvertMaterialsToURP]</color> Converted: {mat.name}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log("<color=green>[ConvertMaterialsToURP]</color> Done! All materials converted to URP/Lit.");
    }
}
