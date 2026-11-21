// カメラ役のライト(レフ板)の MA prefab を作る(開発用)。Assets/Avatar/MyLight/Editor/ に置いてバッチモードで:
//   Unity.exe -batchmode -quit -projectPath "D:\Unity\Unity VRM project 2"
//     -executeMethod MyLightBuilder.Build [-scene Assets/Scenes/Camera_01.unity -avatar "名前"]
// メニュー「ライト」: スポット / 人物だけ(MyLight/Mode = 1 / 2、Int)、影(MyLight/Shadow、Bool)、
// 強さ・高さ・範囲・角度(MyLight/Intensity・Height・Range・Angle、Float、Radial)。
// ライトは首(Neck)に Bone Proxy で付いた Anchor の下の Offset(高さで上下する)の下。
// 頭にしないのは、VRChat が一人称の画面で自分の頭の骨を縮めるので、頭の子の位置のずれが効かないため。
// 「人物だけ」は点光源の Culling Mask を Player・PlayerLocal・MirrorReflection のレイヤーだけにしたもの
// (2026-11-16。前は普通の点光源)。アバターだけが照らされ、床や壁は明るくならない。
// 影の種類はアニメーションできないので、影なし(Spot・Person)と影あり(SpotShadow・PersonShadow)を置き、
// Light の m_Enabled を影のトグルで入れ替える。スポット・人物だけの入り切りは、親 SpotGroup・PersonGroup を Object Toggle で。
// ダイヤルはそれぞれ Merge Blend Tree の 1D(両端のクリップ)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Ctl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

public static class MyLightBuilder
{
    const string Dir = "Assets/Avatar/MyLight";
    const string PrefabPath = Dir + "/MyLight.prefab";
    const string PMode = "MyLight/Mode";
    const string PInt = "MyLight/Intensity";
    const string PHeight = "MyLight/Height";
    const string PRange = "MyLight/Range";
    const string PAngle = "MyLight/Angle";
    const string PShadow = "MyLight/Shadow";
    const string PPitch = "MyLight/Pitch";
    const float SpotMax = 4f, PointMax = 8f;           // 人物だけは背景が飛ばないので強めまで(ブルーム・Glow のしきい値を超えるように)
    const float PointMid = 1.5f;                        // 強さ 0.5 での人物だけ(前の点光源と同じ)。0.5〜1 で 1.5 → 8 と一気に強くなる
    const float HeightDown = 1.5f, HeightUp = 4f;       // 高さ: 0 で -1.5 m、0.5 で 0、1 で +4 m(上からも当てられるよう上に広く)
    const float PitchMax = 90f;                         // 上下の向き(スポット): 0 で真上(あおり)、0.5 で水平、1 で真下
    const float SpotRange0 = 2f, SpotRange1 = 18f;      // 範囲: 0.5 でスポット 10 m・点光源 5 m
    const float PointRange0 = 1f, PointRange1 = 9f;
    const float Angle0 = 20f, Angle1 = 100f;            // 角度: 0.5 で 60 度
    const string OffsetPath = "Anchor/Offset";
    const string SpotPath = "Anchor/Offset/SpotGroup/Spot", SpotSPath = "Anchor/Offset/SpotGroup/SpotShadow";
    const string PointPath = "Anchor/Offset/PersonGroup/Person", PointSPath = "Anchor/Offset/PersonGroup/PersonShadow";
    // Unity の組み込みレイヤー番号(VRChat): Player 9、PlayerLocal 10、MirrorReflection 18
    const int PersonMask = (1 << 9) | (1 << 10) | (1 << 18);

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

    static Ctl NewCtl(string name, Ctl.ControlType type, string param, float value)
    {
        return new Ctl
        {
            name = name, type = type, value = value,
            parameter = new Ctl.Parameter { name = param ?? "" },
            subParameters = Array.Empty<Ctl.Parameter>(),
            labels = Array.Empty<Ctl.Label>(),
        };
    }

    static ModularAvatarMenuItem AddItem(GameObject go, Ctl ctl, string label)
    {
        var item = go.AddComponent<ModularAvatarMenuItem>();
        item.Control = ctl;
        item.label = label;
        item.isSynced = true;
        item.isSaved = false;
        item.isDefault = false;
        item.automaticValue = false;
        return item;
    }

    static AvatarObjectReference Ref(string path, GameObject target)
    {
        var r = new AvatarObjectReference { referencePath = path };
        typeof(AvatarObjectReference).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(r, target);
        return r;
    }

    static AnimationClip Clip(string name, params (string path, Type type, string prop, float v)[] curves)
    {
        var c = new AnimationClip { name = name };
        foreach (var k in curves) c.SetCurve(k.path, k.type, k.prop, AnimationCurve.Constant(0, 1f / 60, k.v));
        var path = Dir + "/Clips/" + name + ".anim";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(c, path);
        return c;
    }

    static BlendTree Tree(string name, string param, AnimationClip at0, AnimationClip at1, AnimationClip atHalf = null)
    {
        var treePath = Dir + "/Clips/" + name + ".asset";
        AssetDatabase.DeleteAsset(treePath);
        var tree = new BlendTree { name = name, blendType = BlendTreeType.Simple1D, blendParameter = param, useAutomaticThresholds = false };
        AssetDatabase.CreateAsset(tree, treePath);
        tree.AddChild(at0, 0f);
        if (atHalf != null) tree.AddChild(atHalf, 0.5f);
        tree.AddChild(at1, 1f);
        return tree;
    }

    static void AddTree(GameObject root, BlendTree tree)
    {
        var mbt = root.AddComponent<ModularAvatarMergeBlendTree>();
        mbt.BlendTree = tree;
        mbt.PathMode = MergeAnimatorPathMode.Relative;   // パスは prefab の根元から
    }

    static GameObject NewLight(Transform parent, string name, LightType type, float range, Vector3 pos, bool shadow, int cullingMask = ~0)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.range = range;
        if (type == LightType.Spot) { l.spotAngle = 60; l.innerSpotAngle = 30; }
        l.color = new Color(1f, 0.96f, 0.9f);
        l.intensity = 1;
        l.shadows = shadow ? LightShadows.Soft : LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
        l.cullingMask = cullingMask;
        l.enabled = !shadow;   // ふだんは影なしの方(影のトグルで入れ替える)
        return go;
    }

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Avatar", "MyLight");

        if (!AssetDatabase.IsValidFolder(Dir + "/Clips")) AssetDatabase.CreateFolder(Dir, "Clips");
        foreach (var old in new[] { "MyLight_Off.anim", "MyLight_Max.anim", "MyLight_Intensity.asset" })
            AssetDatabase.DeleteAsset(Dir + "/" + old);   // 前の版の置き場所
        var L = typeof(Light); var T = typeof(Transform);
        (string, Type, string, float)[] Both(string spotProp, float spotV, string pointProp, float pointV)
        {
            var list = new List<(string, Type, string, float)> { (SpotPath, L, spotProp, spotV), (SpotSPath, L, spotProp, spotV) };
            if (pointProp != null) { list.Add((PointPath, L, pointProp, pointV)); list.Add((PointSPath, L, pointProp, pointV)); }
            return list.ToArray();
        }
        var trees = new[]
        {
            Tree("MyLight_Intensity", PInt,
                Clip("Intensity_0", Both("m_Intensity", 0, "m_Intensity", 0)),
                Clip("Intensity_1", Both("m_Intensity", SpotMax, "m_Intensity", PointMax)),
                Clip("Intensity_Mid", Both("m_Intensity", SpotMax / 2, "m_Intensity", PointMid))),
            Tree("MyLight_Height", PHeight,
                Clip("Height_0", (OffsetPath, T, "m_LocalPosition.y", -HeightDown)),
                Clip("Height_1", (OffsetPath, T, "m_LocalPosition.y", HeightUp)),
                Clip("Height_Mid", (OffsetPath, T, "m_LocalPosition.y", 0))),
            // 上下の向き: スポットのライト自身を x 軸の回りに回す(+ で下向き)
            Tree("MyLight_Pitch", PPitch,
                Clip("Pitch_0", (SpotPath, T, "localEulerAnglesRaw.x", -PitchMax), (SpotSPath, T, "localEulerAnglesRaw.x", -PitchMax)),
                Clip("Pitch_1", (SpotPath, T, "localEulerAnglesRaw.x", PitchMax), (SpotSPath, T, "localEulerAnglesRaw.x", PitchMax))),
            Tree("MyLight_Range", PRange,
                Clip("Range_0", Both("m_Range", SpotRange0, "m_Range", PointRange0)),
                Clip("Range_1", Both("m_Range", SpotRange1, "m_Range", PointRange1))),
            Tree("MyLight_Angle", PAngle,
                Clip("Angle_0", Both("m_SpotAngle", Angle0, null, 0).Concat(Both("m_InnerSpotAngle", Angle0 / 2, null, 0)).ToArray()),
                Clip("Angle_1", Both("m_SpotAngle", Angle1, null, 0).Concat(Both("m_InnerSpotAngle", Angle1 / 2, null, 0)).ToArray())),
            // 影: 影なしのライトと影ありのライトの有効・無効を入れ替える
            Tree("MyLight_Shadow", PShadow,
                Clip("Shadow_0", (SpotPath, L, "m_Enabled", 1), (SpotSPath, L, "m_Enabled", 0), (PointPath, L, "m_Enabled", 1), (PointSPath, L, "m_Enabled", 0)),
                Clip("Shadow_1", (SpotPath, L, "m_Enabled", 0), (SpotSPath, L, "m_Enabled", 1), (PointPath, L, "m_Enabled", 0), (PointSPath, L, "m_Enabled", 1))),
        };

        var root = new GameObject("MyLight");
        root.AddComponent<ModularAvatarMenuInstaller>();   // トップに
        var sub = AddItem(root, NewCtl("ライト", Ctl.ControlType.SubMenu, null, 1), "ライト");
        sub.MenuSource = SubmenuSource.Children;
        var prm = root.AddComponent<ModularAvatarParameters>();
        prm.parameters = new List<ParameterConfig>
        {
            new ParameterConfig { nameOrPrefix = PMode, syncType = ParameterSyncType.Int, defaultValue = 0, saved = false },
            new ParameterConfig { nameOrPrefix = PInt, syncType = ParameterSyncType.Float, defaultValue = 0.5f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PHeight, syncType = ParameterSyncType.Float, defaultValue = 0.5f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PRange, syncType = ParameterSyncType.Float, defaultValue = 0.5f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PAngle, syncType = ParameterSyncType.Float, defaultValue = 0.5f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PPitch, syncType = ParameterSyncType.Float, defaultValue = 0.5f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PShadow, syncType = ParameterSyncType.Bool, defaultValue = 0, saved = false },
        };
        foreach (var t in trees) AddTree(root, t);

        var anchor = new GameObject("Anchor");
        anchor.transform.SetParent(root.transform, false);
        var bp = anchor.AddComponent<ModularAvatarBoneProxy>();
        bp.boneReference = HumanBodyBones.Neck;
        bp.attachmentMode = BoneProxyAttachmentMode.AsChildAtRoot;
        var offset = new GameObject("Offset");
        offset.transform.SetParent(anchor.transform, false);
        GameObject Group(string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(offset.transform, false);
            g.SetActive(false);   // メニューで選んだときだけ点く(Object Toggle)
            return g;
        }
        var spot = Group("SpotGroup");
        var point = Group("PersonGroup");
        // 首から: スポットは上 15 cm・前 15 cm(頭の前あたり)、人物だけは上 20 cm・前 30 cm
        foreach (var sh in new[] { false, true })
        {
            NewLight(spot.transform, sh ? "SpotShadow" : "Spot", LightType.Spot, 10, new Vector3(0, 0.15f, 0.15f), sh);
            NewLight(point.transform, sh ? "PersonShadow" : "Person", LightType.Point, 5, new Vector3(0, 0.2f, 0.3f), sh, PersonMask);
        }

        GameObject Entry(string objName, string label, Ctl ctl, GameObject light)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(root.transform, false);
            AddItem(go, ctl, label);
            if (light != null)
            {
                var t = go.AddComponent<ModularAvatarObjectToggle>();
                t.Objects = new List<ToggledObject> { new ToggledObject { Object = Ref("MyLight/" + OffsetPath + "/" + light.name, light), Active = true } };
            }
            return go;
        }
        Entry("Spot", "スポット", NewCtl("スポット", Ctl.ControlType.Toggle, PMode, 1), spot);
        Entry("Person", "人物だけ", NewCtl("人物だけ", Ctl.ControlType.Toggle, PMode, 2), point);
        var radial = NewCtl("強さ", Ctl.ControlType.RadialPuppet, null, 1);
        radial.subParameters = new[] { new Ctl.Parameter { name = PInt } };
        Entry("Shadow", "影", NewCtl("影", Ctl.ControlType.Toggle, PShadow, 1), null);
        Entry("Intensity", "強さ", radial, null);
        foreach (var (obj, label, prm2) in new[] { ("Height", "高さ", PHeight), ("Range", "範囲", PRange), ("Angle", "角度(スポット)", PAngle), ("Pitch", "上下の向き(スポット)", PPitch) })
        {
            var rc = NewCtl(label, Ctl.ControlType.RadialPuppet, null, 1);
            rc.subParameters = new[] { new Ctl.Parameter { name = prm2 } };
            Entry(obj, label, rc, null);
        }

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[MyLight] prefab: " + PrefabPath);

        var scene = Arg("-scene"); var avatar = Arg("-avatar");
        if (scene != null && avatar != null)
        {
            var sc = EditorSceneManager.OpenScene(scene);
            GameObject av = null;
            foreach (var r in sc.GetRootGameObjects()) if (r.name == avatar) av = r;
            if (av == null) throw new Exception("avatar not found: " + avatar);
            if (av.transform.Find("MyLight") == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, av.transform);
                inst.name = "MyLight";
            }
            EditorSceneManager.SaveScene(sc);
            Debug.Log("[MyLight] placed under " + avatar + " in " + scene);
        }
    }

    // 確かめ用: アバターを複製して NDMF を通し、メニュー・パラメーター・ライトの場所・FX を記録に出す。
    public static void Verify()
    {
        var sc = EditorSceneManager.OpenScene(Arg("-scene"));
        GameObject root = null;
        foreach (var r in sc.GetRootGameObjects()) if (r.name == Arg("-avatar")) root = r;
        var clone = UnityEngine.Object.Instantiate(root);
        nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(clone);
        var desc = clone.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        void Dump(VRCExpressionsMenu m, string indent)
        {
            foreach (var c in m.controls)
            {
                var sp = c.subParameters != null && c.subParameters.Length > 0 ? " sub=" + c.subParameters[0].name : "";
                Debug.Log("[Verify] menu: " + indent + c.name + " type=" + c.type + " param=" + c.parameter?.name + " value=" + c.value + sp);
                if (c.type == Ctl.ControlType.SubMenu && c.subMenu != null && indent.Length < 2 && (c.name == "ライト" || indent == "")) { if (c.name == "ライト") Dump(c.subMenu, indent + "  "); }
            }
        }
        Dump(desc.expressionsMenu, "");
        int bits = 0;
        foreach (var p in desc.expressionParameters.parameters)
        {
            if (p.networkSynced) bits += p.valueType == VRCExpressionParameters.ValueType.Bool ? 1 : 8;
            if (p.name.StartsWith("MyLight")) Debug.Log("[Verify] param: " + p.name + " " + p.valueType + " default=" + p.defaultValue + " synced=" + p.networkSynced + " saved=" + p.saved);
        }
        Debug.Log("[Verify] synced bits = " + bits);
        foreach (var l in clone.GetComponentsInChildren<Light>(true))
        {
            var t = l.transform; var path = t.name;
            while (t.parent != null && t.parent != clone.transform) { t = t.parent; path = t.name + "/" + path; }
            Debug.Log("[Verify] light: " + path + " " + l.type + " active=" + l.gameObject.activeSelf + " mask=0x" + l.cullingMask.ToString("x") + " pos=" + l.transform.position + " fwd=" + l.transform.forward);
        }
        foreach (var l in desc.baseAnimationLayers)
            if (l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX && l.animatorController is AnimatorController ac)
                foreach (var al in ac.layers)
                    if (al.name.Contains("MA") || al.name.Contains("Modular") || al.name.Contains("Light"))
                    {
                        Debug.Log("[Verify] FX layer: " + al.name);
                        foreach (var st in al.stateMachine.states)
                            if (st.state.motion is BlendTree bt) DumpTree(bt, "  ");
                    }
        UnityEngine.Object.DestroyImmediate(clone);
    }

    static void DumpTree(BlendTree bt, string ind)
    {
        foreach (var ch in bt.children)
        {
            if (ch.motion is BlendTree sub) { Debug.Log("[Verify] " + ind + "tree " + sub.name + " param=" + sub.blendParameter); DumpTree(sub, ind + "  "); }
            else if (ch.motion is AnimationClip c)
                foreach (var b in AnimationUtility.GetCurveBindings(c))
                    if (b.type == typeof(Light) || b.type == typeof(Transform)) Debug.Log("[Verify] " + ind + c.name + ": " + b.path + " " + b.propertyName + " = " + AnimationUtility.GetEditorCurve(c, b).keys[0].value);
        }
    }

    // 調べ用: 処理後のアバターで、ライトと Offset のアニメーションできる値を出し、クリップを当てて動くかを見る。
    public static void Probe()
    {
        var sc = EditorSceneManager.OpenScene(Arg("-scene"));
        GameObject root = null;
        foreach (var r in sc.GetRootGameObjects()) if (r.name == Arg("-avatar")) root = r;
        var clone = UnityEngine.Object.Instantiate(root);
        nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(clone);
        var spot = clone.GetComponentsInChildren<Light>(true)[0];
        foreach (var b in AnimationUtility.GetAnimatableBindings(spot.gameObject, clone))
            if (b.type == typeof(Light) || b.type == typeof(Transform)) Debug.Log("[Probe] spot bindable: " + b.type.Name + " " + b.propertyName);
        var offset = spot.transform.parent.parent.gameObject;
        foreach (var b in AnimationUtility.GetAnimatableBindings(offset, clone))
            Debug.Log("[Probe] offset bindable: " + b.type.Name + " " + b.propertyName);
        var desc = clone.GetComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
        foreach (var l in desc.baseAnimationLayers)
            if (l.type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX && l.animatorController is AnimatorController ac)
                foreach (var c in ac.animationClips)
                    if (c.name.StartsWith("Height_") || c.name.StartsWith("Pitch_"))
                    {
                        c.SampleAnimation(clone, 0);
                        Debug.Log("[Probe] sampled " + c.name + ": offset local=" + offset.transform.localPosition + " spot world=" + spot.transform.position + " spot fwd=" + spot.transform.forward + " lights=" + string.Join(",", clone.GetComponentsInChildren<Light>(true).Select(x => x.name + (x.enabled ? ":on" : ":off") + "/" + x.shadows)));
                        foreach (var b in AnimationUtility.GetCurveBindings(c))
                        {
                            var o = AnimationUtility.GetAnimatedObject(clone, b);
                            Debug.Log("[Probe]   " + b.path + " " + b.type.Name + "." + b.propertyName + " -> " + (o != null ? o.ToString() : "NULL"));
                        }
                    }
        UnityEngine.Object.DestroyImmediate(clone);
    }
}
