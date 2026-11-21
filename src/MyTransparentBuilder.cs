// 透明化の MA prefab を作る(開発用)。Assets/Avatar/MyTransparent/Editor/ に置いてバッチモードで:
//   Unity.exe -batchmode -quit -projectPath "D:\Unity\Unity VRM project 2"
//     -executeMethod MyTransparentBuilder.Build [-scene Assets/Scenes/Camera_01.unity -avatar "名前"]
// メッシュ "Body"(アバターの根元からのパス)を、トグルが ON のとき非表示にする。
// 2026-11-21(2 回目、JRF が Gemini から聞いた案): 透明にすると描く物がなくなり、ほかの人の画面でアニメーションの更新が
// 止まって OFF が伝わらない。→ 出し入れしない「完全に透明(アルファ 0)」の箱 KeepVisible/Dummy を腰に置く。
// 描画は走るが画面には何も書かないので、撮影に映らない(はず)。前の暗い点(d3698e3)は見えたのでやめた。
using System;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

public static class MyTransparentBuilder
{
    const string Dir = "Assets/Avatar/MyTransparent";
    const string PrefabPath = Dir + "/MyTransparent.prefab";
    const string Param = "MyTransparent/On";
    const float DummySize = 0.3f;   // 透明の箱の大きさ(m)。画面に入っていることが要るので、点より大きめに

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

    public static void Build()
    {
        var go = new GameObject("MyTransparent");
        go.AddComponent<ModularAvatarMenuInstaller>();   // installTargetMenu なし = トップ
        var item = go.AddComponent<ModularAvatarMenuItem>();
        item.Control = new VRCExpressionsMenu.Control
        {
            name = "透明",
            type = VRCExpressionsMenu.Control.ControlType.Toggle,
            parameter = new VRCExpressionsMenu.Control.Parameter { name = Param },
            value = 1,
            subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
            labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
        };
        item.label = "透明";
        item.isSynced = true;
        item.isSaved = false;
        item.isDefault = false;
        item.automaticValue = false;
        var tog = go.AddComponent<ModularAvatarObjectToggle>();
        tog.Objects = new List<ToggledObject>
        {
            new ToggledObject { Object = new AvatarObjectReference { referencePath = "Body" }, Active = false },
        };
        var keep = new GameObject("KeepVisible");
        keep.transform.SetParent(go.transform, false);
        var bp = keep.AddComponent<ModularAvatarBoneProxy>();
        bp.boneReference = HumanBodyBones.Hips;
        bp.attachmentMode = BoneProxyAttachmentMode.AsChildAtRoot;
        var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(dummy.GetComponent<Collider>());
        dummy.name = "Dummy";
        dummy.transform.SetParent(keep.transform, false);
        dummy.transform.localScale = Vector3.one * DummySize;
        var matPath = Dir + "/KeepVisible_Alpha0.mat";
        AssetDatabase.DeleteAsset(matPath);
        var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0f) };
        AssetDatabase.CreateAsset(mat, matPath);
        var mr = dummy.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        UnityEngine.Object.DestroyImmediate(go);
        Debug.Log("[MyTransparent] prefab: " + PrefabPath);

        var scene = Arg("-scene"); var avatar = Arg("-avatar");
        if (scene != null && avatar != null)
        {
            var sc = EditorSceneManager.OpenScene(scene);
            GameObject root = null;
            foreach (var r in sc.GetRootGameObjects()) if (r.name == avatar) root = r;
            if (root == null) throw new Exception("avatar not found: " + avatar);
            if (root.transform.Find("MyTransparent") == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                inst.name = "MyTransparent";
            }
            EditorSceneManager.SaveScene(sc);
            Debug.Log("[MyTransparent] placed under " + avatar + " in " + scene);
        }
    }

    // 確かめ用: シーンのアバターを複製して NDMF(MA)を通し、トップメニュー・パラメーター・FX のレイヤーを記録に出す。
    public static void Verify()
    {
        var sc = EditorSceneManager.OpenScene(Arg("-scene"));
        GameObject root = null;
        foreach (var r in sc.GetRootGameObjects()) if (r.name == Arg("-avatar")) root = r;
        var clone = UnityEngine.Object.Instantiate(root);
        nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(clone);
        var desc = clone.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        foreach (var c in desc.expressionsMenu.controls)
            Debug.Log("[Verify] menu: " + c.name + " type=" + c.type + " param=" + c.parameter?.name);
        int bits = 0;
        foreach (var p in desc.expressionParameters.parameters)
        {
            if (p.networkSynced) bits += p.valueType == VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool ? 1 : 8;
            if (p.name.Contains("Transparent")) Debug.Log("[Verify] param: " + p.name + " synced=" + p.networkSynced + " saved=" + p.saved);
        }
        Debug.Log("[Verify] synced bits = " + bits);
        foreach (var l in desc.baseAnimationLayers)
            if (l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX && l.animatorController is UnityEditor.Animations.AnimatorController ac)
                foreach (var al in ac.layers) Debug.Log("[Verify] FX layer: " + al.name);
        foreach (var mr in clone.GetComponentsInChildren<Renderer>(true))
            Debug.Log("[Verify] renderer: " + PathOf(mr.transform, clone.transform) + " self=" + mr.gameObject.activeSelf + " rootActive=" + clone.activeSelf
                + " mat=" + (mr.sharedMaterial ? mr.sharedMaterial.shader.name + " a=" + (mr.sharedMaterial.HasProperty("_Color") ? mr.sharedMaterial.color.a : -1) : "-"));
        UnityEngine.Object.DestroyImmediate(clone);
    }

    static string PathOf(Transform t, Transform root)
    {
        var s = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
