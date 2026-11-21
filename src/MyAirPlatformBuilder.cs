// ほかの人を乗せる空中の床(MyAirPlatform)の MA prefab を作る(開発用)。Assets/Avatar/MyAirPlatform/Editor/ に置いてバッチモードで:
//   Unity.exe -batchmode -quit -projectPath "D:\Unity\Unity VRM project 2"
//     -executeMethod MyAirPlatformBuilder.Build [-scene Assets/Scenes/Camera_01.unity -avatar "名前"]
//   MyAirPlatformBuilder.Verify -scene … -avatar …
// 経緯(2026-11-21): 自分のアバターのコライダーは自分の体を支えなかった(MyAirFloor。接地にはなるが沈む)。
// ほかの人のアバターのコライダーなら乗れる、という話があるので、床をカメラ役などのアバターに付け、同期して、
// ほかの人(ジョルジュ)をその上に乗せる。未確認。
// メニュー「空中の椅子」(サブメニュー):
//   トグル「床を置く」(MyAirPlatform/On、Bool、同期あり・保存なし): 置いた瞬間の自分の足元(の前・上)にワールドで固定。
//     Off → Place(0.1 秒ついてくる)→ Hold(VRC Parent Constraint の Freeze To World)。切ると消える。
//   Radial「高さ」(MyAirPlatform/Height、Float、同期あり・保存あり、既定 0): 0 で足元、1 で 5 m 上。置いたあとも動かせる
//     (固定した Anchor の子の Floor の高さを動かす)。
//   Radial「前へ」(MyAirPlatform/Forward、Float、同期あり・保存あり、既定 0.3): 床の真ん中を 0〜5 m 前に(置いたあとも動く)。
// (2026-11-21 に床から椅子に作り替えた。下の Build の中を参照)。メニューの名前は「空中の椅子」「椅子を置く」。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using Ctl = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control;

public static class MyAirPlatformBuilder
{
    const string Dir = "Assets/Avatar/MyAirPlatform";
    const string PrefabPath = Dir + "/MyAirPlatform.prefab";
    const string FxPath = Dir + "/MyAirPlatform_FX.controller";
    const string POn = "MyAirPlatform/On", PHeight = "MyAirPlatform/Height",
        PForward = "MyAirPlatform/Forward", PColor = "MyAirPlatform/Color", PSlot = "MyAirPlatform/Replace";
    const string AnchorPath = "Anchor", FloorPath = "Anchor/Floor", SeatPath = "Anchor/Floor/Seat";
    // 高さ: 「高さ」0〜20 m + 「高さ(細かく)」0〜1 m(2026-11-21、JRF「もっと高く」。8 bit なので 20 m を 1 本だと約 8 cm 刻み)
    const float HeightMax = 20f, ForwardMax = 5f;
    const float KickDelay = 2f;   // 出してから「置き直す」を反転させるまで(秒)

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

    static AnimationClip Save(AnimationClip c, string name) { c.name = name; AssetDatabase.CreateAsset(c, Dir + "/" + name + ".anim"); return c; }

    static AnimationClip StateClip(string name, bool active, bool freeze, float len)
    {
        var c = new AnimationClip();
        var k = new Func<float, AnimationCurve>(v => new AnimationCurve(new Keyframe(0, v), new Keyframe(len, v)));
        // 2026-11-21: GlobalWeight 版・World Fixed Object 版も、ほかの人の画面で出し直しが伝わらない(アバターのリセットが要る)のは
        // 同じだった。JRF の判断で最初の作り(Anchor の GameObject を出し入れし、Freeze To World で固定)に戻した
        // (天井を突き抜けて置けた、高さが座った人の視点にそのまま効いた、など、使い勝手は最初のものがよかった)。
        // 2026-11-21(2): 「椅子を出す」の OFF → ON では、ほかの人の画面で新しい場所が伝わらないことが多かった(椅子を見ていないとき)。
        // 「置き直す」(Anchor が出たまま固定を外して付け直す)なら伝わった。→ Anchor はいつも出したまま、OFF の間は固定せず
        // 椅子役についていかせ、出し入れは子の Floor で。出すときは「置き直す」と同じく、ついてきている Anchor を固定するだけになる。
        c.SetCurve(FloorPath, typeof(GameObject), "m_IsActive", k(active ? 1 : 0));
        c.SetCurve(AnchorPath, typeof(VRCParentConstraint), "FreezeToWorld", k(freeze ? 1 : 0));
        return Save(c, name);
    }

    static string KickState(Animator anim)
    {
        int li = anim.GetLayerIndex("MyAirPlatform Kick");
        if (li < 0) return "-";
        var info = anim.GetCurrentAnimatorStateInfo(li);
        foreach (var n in new[] { "Idle", "Delay", "FlipTo1", "FlipTo0" }) if (info.IsName(n)) return n;
        return "?";
    }

    static AnimationClip PosClip(string name, string path, string axis, float v)
    {
        var c = new AnimationClip();
        c.SetCurve(path, typeof(Transform), "m_LocalPosition." + axis, AnimationCurve.Constant(0, 1f / 60, v));
        return Save(c, name);
    }

    static ModularAvatarMenuItem Item(GameObject parent, string goName, string label, Ctl.ControlType type, string param, string sub = null)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(parent.transform, false);
        var mi = go.AddComponent<ModularAvatarMenuItem>();
        mi.Control = new Ctl
        {
            name = label, type = type, value = 1,
            parameter = new Ctl.Parameter { name = param ?? "" },
            subParameters = sub == null ? Array.Empty<Ctl.Parameter>() : new[] { new Ctl.Parameter { name = sub } },
        };
        mi.label = label;
        return mi;
    }

    static void MergeTree(GameObject root, string name, string param, AnimationClip a, AnimationClip b)
    {
        var tree = new BlendTree { name = name, blendType = BlendTreeType.Simple1D, blendParameter = param, useAutomaticThresholds = false };
        AssetDatabase.CreateAsset(tree, Dir + "/" + name + ".asset");
        tree.AddChild(a, 0f); tree.AddChild(b, 1f);
        EditorUtility.SetDirty(tree);
        var mbt = root.AddComponent<ModularAvatarMergeBlendTree>();
        mbt.BlendTree = tree;
        mbt.PathMode = MergeAnimatorPathMode.Relative;
    }

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Avatar", "MyAirPlatform");
        foreach (var f in AssetDatabase.FindAssets("", new[] { Dir }).Select(AssetDatabase.GUIDToAssetPath)
                     .Where(x => x.EndsWith(".anim") || x.EndsWith(".mat") || x.EndsWith(".asset")).ToList()) AssetDatabase.DeleteAsset(f);
        AssetDatabase.DeleteAsset(FxPath);

        // 2026-11-21(3): VRChat で試すと、ジョルジュが椅子役を見ていないときの OFF → ON だけ失敗し、見ていないときの「置き直す」と
        // 見ているときの OFF → ON は効いた。見立て: 見られていない間、ほかの人の画面では Anchor の追従が更新されず、固定を外したまま
        // にしておくと、ON の固定がその古い位置で固まる。固定を外した瞬間には位置を計算し直すらしい(置き直すが効く理由)。
        // → OFF の間も固定したまま(Floor は消す)にし、ON で「置き直す」と同じく 固定 → 外す(0.1 秒)→ 固定 にする。
        var off = StateClip("Platform_Off", false, true, 1f / 60);
        var place = StateClip("Platform_Place", true, false, 0.1f);
        var hold = StateClip("Platform_Hold", true, true, 1f / 60);

        var ac = AnimatorController.CreateAnimatorControllerAtPath(FxPath);
        ac.AddParameter(POn, AnimatorControllerParameterType.Bool);
        ac.AddParameter(PSlot, AnimatorControllerParameterType.Bool);
        var sm = ac.layers[0].stateMachine;
        var layers = ac.layers; layers[0].name = "MyAirPlatform"; layers[0].defaultWeight = 1; ac.layers = layers;
        AnimatorState S(string n, Motion m) { var s = sm.AddState(n); s.motion = m; s.writeDefaultValues = false; return s; }
        // 2026-11-21: 置き直しを「OFF → ON」で伝えると、ほかの人の画面で取りこぼされ、椅子役がアバターをリセットするまで
        // 前の場所に残った(と見た)。→ トグル「置き直す」(MyAirPlatform/Replace)で、押すたびに値が反転し、反転するたびに
        // 置き直す(A ⇄ B の 2 組の Place → Hold)。値は残るので、ほかの人の画面でも最後の値を受け取った時点で置き直される。
        var sOff = S("Off", off);
        var sPlaceA = S("PlaceA", place); var sHoldA = S("HoldA", hold);
        var sPlaceB = S("PlaceB", place); var sHoldB = S("HoldB", hold);
        sm.defaultState = sOff;
        AnimatorStateTransition T(AnimatorState a, AnimatorState b) { var t = a.AddTransition(b); t.hasExitTime = false; t.duration = 0; return t; }
        // 2026-11-21(4): OFF の間も固定したままにしても、見られていない間の OFF → ON は古い場所のまま固まった。
        // ON の 0.2 秒後に手で「置き直す」を反転させると、見ていない人にも新しい場所が伝わった(Claude が VRChat で確かめた)。
        // → ON にしたら、アバター自身が「置き直す」を反転させる(Parameter Driver、localOnly。同期でほかの人にも届く)。
        // 自分の画面: Off → KickA(=PlaceA の動き + Slot を 1 に)→ HoldA → (Slot 1)PlaceB → HoldB。
        // ほかの人の画面: Off → KickA → HoldA で、Slot の反転が届いたら PlaceB → HoldB(手で「置き直す」を押したのと同じ)。
        // 2026-11-21(7): JRF の試し: 見ていない間に出す → 古い場所。見ていない間に「置き直す」を 1 回 → 効く、2 回 → 効かない。
        // ほかの人の画面で効くのは「椅子が Hold で落ち着いたあとに届いた Slot の変化」だけらしい(時間で進む置き直しは効かない)。
        // (6) の版は出すときと 1.7 秒後に 2 回反転させていて、ほかの人の画面ではその間ちょうど置き直しの途中(Slot を聞いていない)
        // で、差し引き変化なしになっていた。→ 主の層の Kick では反転させず、別の層「MyAirPlatform Kick」が出してから 2 秒後に
        // 1 回だけ反転させる(手で「置き直す」を 1 回押したのと同じ)。出すを連打しても、最後に出してから 2 秒後に 1 回だけ。
        var sKickA = S("KickA", place); var sKickB = S("KickB", place);
        var t0 = T(sOff, sKickA); t0.AddCondition(AnimatorConditionMode.If, 0, POn); t0.AddCondition(AnimatorConditionMode.IfNot, 0, PSlot);
        var t1 = T(sOff, sKickB); t1.AddCondition(AnimatorConditionMode.If, 0, POn); t1.AddCondition(AnimatorConditionMode.If, 0, PSlot);
        var wait = StateClip("Platform_Wait", true, true, 0.5f);
        AnimatorStateTransition E(AnimatorState a, AnimatorState b) { var t = T(a, b); t.hasExitTime = true; t.exitTime = 1f; return t; }
        var chain = new List<AnimatorState> { sKickA, sKickB };
        foreach (var (kick, ab, hold2) in new[] { (sKickA, "A", sHoldA), (sKickB, "B", sHoldB) })
        {
            var sWait = S("Wait" + ab, wait); var sPlace2 = S("Place2" + ab, place);
            E(kick, sWait); E(sWait, sPlace2); E(sPlace2, hold2);
            chain.AddRange(new[] { sWait, sPlace2 });
        }
        foreach (var st0 in chain) T(st0, sOff).AddCondition(AnimatorConditionMode.IfNot, 0, POn);

        // 反転の層(重み 0。動きは書かない。Parameter Driver は重みに関係なく動く。localOnly なので、ほかの人の画面では何もしない)。
        var delay = new AnimationClip();
        delay.SetCurve("_MyAirPlatformDelay", typeof(GameObject), "m_IsActive", new AnimationCurve(new Keyframe(0, 0), new Keyframe(KickDelay, 0)));
        Save(delay, "Platform_KickDelay");
        ac.AddLayer("MyAirPlatform Kick");
        var kl = ac.layers; kl[1].defaultWeight = 0; ac.layers = kl;
        var ksm = ac.layers[1].stateMachine;
        AnimatorState K(string n, Motion m) { var st = ksm.AddState(n); st.motion = m; st.writeDefaultValues = false; return st; }
        var kIdle = K("Idle", null); var kDelay = K("Delay", delay);
        var kTo1 = K("FlipTo1", null); var kTo0 = K("FlipTo0", null);
        ksm.defaultState = kIdle;
        foreach (var (kst, v) in new[] { (kTo1, 1f), (kTo0, 0f) })
        {
            var d = kst.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            d.localOnly = true;
            d.parameters = new List<VRC.SDKBase.VRC_AvatarParameterDriver.Parameter>
            {
                new VRC.SDKBase.VRC_AvatarParameterDriver.Parameter { type = VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set, name = PSlot, value = v },
            };
        }
        T(kIdle, kDelay).AddCondition(AnimatorConditionMode.If, 0, POn);
        var kd1 = E(kDelay, kTo1); kd1.AddCondition(AnimatorConditionMode.IfNot, 0, PSlot);
        var kd0 = E(kDelay, kTo0); kd0.AddCondition(AnimatorConditionMode.If, 0, PSlot);
        foreach (var st0 in new[] { kDelay, kTo1, kTo0 }) T(st0, kIdle).AddCondition(AnimatorConditionMode.IfNot, 0, POn);
        var ta = T(sPlaceA, sHoldA); ta.hasExitTime = true; ta.exitTime = 1f;
        var tb = T(sPlaceB, sHoldB); tb.hasExitTime = true; tb.exitTime = 1f;
        T(sHoldA, sPlaceB).AddCondition(AnimatorConditionMode.If, 0, PSlot);
        T(sHoldB, sPlaceA).AddCondition(AnimatorConditionMode.IfNot, 0, PSlot);
        foreach (var st0 in new[] { sPlaceA, sHoldA, sPlaceB, sHoldB }) T(st0, sOff).AddCondition(AnimatorConditionMode.IfNot, 0, POn);
        EditorUtility.SetDirty(ac);

        var root = new GameObject("MyAirPlatform");
        root.AddComponent<ModularAvatarMenuInstaller>();   // トップに
        var sub = root.AddComponent<ModularAvatarMenuItem>();
        sub.Control = new Ctl { name = "空中の椅子", type = Ctl.ControlType.SubMenu, value = 1, parameter = new Ctl.Parameter { name = "" }, subParameters = Array.Empty<Ctl.Parameter>() };
        sub.label = "空中の椅子";
        sub.MenuSource = SubmenuSource.Children;
        var onItem = Item(root, "ItemOn", "椅子を出す", Ctl.ControlType.Toggle, POn);
        // 2026-11-21: 「置き直す」はメニューから外した(JRF)。出してから 2 秒後に中で 1 回だけ反転させるので、手で押すと打ち消し合う。
        // パラメーター(PSlot)は自動の反転に使うので、下の MA Parameters には残す。
        Item(root, "ItemHeight", "高さ(20 m)", Ctl.ControlType.RadialPuppet, null, PHeight);
        Item(root, "ItemForward", "前へ", Ctl.ControlType.RadialPuppet, null, PForward);
        var colorItem = Item(root, "ItemColor", "色を付ける", Ctl.ControlType.Toggle, PColor);
        colorItem.isDefault = true;
        var prm = root.AddComponent<ModularAvatarParameters>();
        prm.parameters = new List<ParameterConfig>
        {
            new ParameterConfig { nameOrPrefix = POn, syncType = ParameterSyncType.Bool, defaultValue = 0, saved = false },
            new ParameterConfig { nameOrPrefix = PSlot, syncType = ParameterSyncType.Bool, defaultValue = 0, saved = false },
            new ParameterConfig { nameOrPrefix = PHeight, syncType = ParameterSyncType.Float, defaultValue = 0f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PForward, syncType = ParameterSyncType.Float, defaultValue = 0.3f, saved = true, hasExplicitDefaultValue = true },
            new ParameterConfig { nameOrPrefix = PColor, syncType = ParameterSyncType.Bool, defaultValue = 1, saved = true, hasExplicitDefaultValue = true },
        };
        var ma = root.AddComponent<ModularAvatarMergeAnimator>();
        ma.animator = ac;
        ma.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
        ma.pathMode = MergeAnimatorPathMode.Relative;
        ma.matchAvatarWriteDefaults = true;
        ma.deleteAttachedAnimator = true;
        MergeTree(root, "Platform_HeightTree", PHeight, PosClip("Platform_H0", FloorPath, "y", 0f), PosClip("Platform_H1", FloorPath, "y", HeightMax));
        MergeTree(root, "Platform_ForwardTree", PForward, PosClip("Platform_F0", FloorPath, "z", 0f), PosClip("Platform_F1", FloorPath, "z", ForwardMax));

        var anchor = new GameObject("Anchor");
        anchor.transform.SetParent(root.transform, false);
        var pc = anchor.AddComponent<VRCParentConstraint>();
        pc.Sources.Add(new VRCConstraintSource { SourceTransform = root.transform, Weight = 1 });
        pc.IsActive = true; pc.Locked = true; pc.FreezeToWorld = false;
        var floorGo = new GameObject("Floor");
        floorGo.transform.SetParent(anchor.transform, false);
        floorGo.transform.localPosition = new Vector3(0, 0, 0.3f * ForwardMax);
        var floor = new GameObject("Seat");
        floor.transform.SetParent(floorGo.transform, false);
        // 2026-11-21: 床はほかの人の体も支えなかった(素通り)。→ 椅子(VRC Station)に作り替えた。
        // 椅子は「乗られる側」のアバター(カメラ役)に付け、ほかの人(ジョルジュ)がクリックして座る
        // (自分のアバターの当たり判定はクリックを素通りするが、ほかの人のアバターのものは押せる)。
        // 当たり判定は座る点のまわりの 1 m の箱(トリガー)。座る点は Floor の位置(高さ・前へのダイヤルで動く)。
        var box = floor.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 1f, 1f);
        box.center = new Vector3(0, 0.5f, 0);
        var st = floor.AddComponent<VRCStation>();
        st.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.Immobilize;
        st.seated = true;
        st.disableStationExit = false;
        st.canUseStationFromStation = false;
        st.stationEnterPlayerLocation = floor.transform;
        st.stationExitPlayerLocation = floor.transform;
        var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(vis.GetComponent<Collider>());
        vis.name = "Visual";
        vis.transform.SetParent(floor.transform, false);
        vis.transform.localScale = new Vector3(0.5f, 0.05f, 0.5f);
        vis.transform.localPosition = new Vector3(0, -0.025f, 0);
        var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.55f, 0.1f) };
        AssetDatabase.CreateAsset(mat, Dir + "/Platform_Visual.mat");
        vis.GetComponent<MeshRenderer>().sharedMaterial = mat;
        vis.SetActive(false);
        var ot = colorItem.gameObject.AddComponent<ModularAvatarObjectToggle>();
        var vref = new AvatarObjectReference { referencePath = "MyAirPlatform/" + SeatPath + "/Visual" };
        typeof(AvatarObjectReference).GetField("targetObject", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(vref, vis);
        ot.Objects.Add(new ToggledObject { Object = vref, Active = true });
        // 椅子の GameObject(Floor の親 Anchor)は上げるときアクティブにしておく(アバターの椅子の決まり。出し入れは FX で)
        anchor.SetActive(true);

        // 2026-11-21: 椅子を出している間、椅子役が姿を消して腰の点だけになる仕掛けを入れていたが、
        // MyTransparent の完全に透明な箱(アルファ 0)で透明化そのものが伝わるようになったので外した(JRF)。
        AssetDatabase.DeleteAsset(Dir + "/Platform_Dot.mat");

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[MyAirPlatform] prefab: " + PrefabPath);

        var scene = Arg("-scene"); var avatar = Arg("-avatar");
        if (scene != null && avatar != null)
        {
            var sc = EditorSceneManager.OpenScene(scene);
            var av = sc.GetRootGameObjects().First(g => g.name == avatar);
            if (av.transform.Cast<Transform>().Any(t => t.name == "MyAirPlatform")) Debug.Log("[MyAirPlatform] もう置いてあります");
            else
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, av.transform);
                inst.name = "MyAirPlatform";
                Debug.Log("[MyAirPlatform] placed under " + avatar + " in " + scene);
            }
            EditorSceneManager.SaveScene(sc);
        }
    }

    // ジョルジュから空中の椅子(MyAirChair)を外す: -executeMethod MyAirPlatformBuilder.RemoveChild -scene … -avatar … -child MyAirChair
    public static void RemoveChild()
    {
        var sc = EditorSceneManager.OpenScene(Arg("-scene"));
        var av = sc.GetRootGameObjects().First(g => g.name == Arg("-avatar"));
        var ch = av.transform.Cast<Transform>().FirstOrDefault(t => t.name == Arg("-child"));
        if (ch == null) { Debug.Log("[MyAirPlatform] ありません: " + Arg("-child")); return; }
        UnityEngine.Object.DestroyImmediate(ch.gameObject);
        EditorSceneManager.SaveScene(sc);
        Debug.Log("[MyAirPlatform] 外しました: " + Arg("-child"));
    }

    public static void Verify()
    {
        var o = new List<string>();
        var sc = EditorSceneManager.OpenScene(Arg("-scene"));
        var r = sc.GetRootGameObjects().First(g => g.name == Arg("-avatar"));
        var clone = UnityEngine.Object.Instantiate(r); clone.SetActive(true);
        nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(clone);
        var d = clone.GetComponent<VRCAvatarDescriptor>();
        o.Add("top: " + string.Join(",", d.expressionsMenu.controls.Select(c => c.name)));
        var subm = d.expressionsMenu.controls.FirstOrDefault(c => c.name == "空中の椅子")?.subMenu;
        if (subm) o.Add("menu: " + string.Join(" / ", subm.controls.Select(c => c.name + "=" + (c.parameter?.name ?? "") + (c.subParameters != null && c.subParameters.Length > 0 ? c.subParameters[0].name : ""))));
        o.Add("bits: " + d.expressionParameters.CalcTotalCost());
        foreach (var p in d.expressionParameters.parameters.Where(p => p.name.StartsWith("MyAirPlatform"))) o.Add($"param {p.name} {p.valueType} synced={p.networkSynced} saved={p.saved} default={p.defaultValue}");
        var fx = d.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
        for (int i = 0; i < fx.layers.Length; i++)
        {
            var L = fx.layers[i];
            if (L.name.Contains("MyAirPlatform") || L.stateMachine.states.Any(s => s.state.motion is BlendTree bt && bt.name.Contains("Platform")))
                o.Add($"fx[{i}] {L.name} w={L.defaultWeight}");
        }
        var anchor = clone.GetComponentsInChildren<Transform>(true).First(t => t.name == "Anchor" && t.GetComponent<VRCParentConstraint>());
        o.Add("anchor path: " + string.Join("/", anchor.GetComponentsInParent<Transform>(true).Reverse().Skip(1).Select(t => t.name)));
        var floorT = anchor.Find("Floor");
        var floor = floorT.Find("Seat");
        var pc = anchor.GetComponent<VRCParentConstraint>();
        var stv = floor.GetComponent<VRCStation>(); var bx = floor.GetComponent<BoxCollider>();
        o.Add($"station: {(stv ? $"seated={stv.seated} mobility={stv.PlayerMobility} enter={stv.stationEnterPlayerLocation?.name}" : "なし")} hit trigger={bx.isTrigger} size={bx.size} center={bx.center}");
        var anim = clone.GetComponent<Animator>();
        anim.runtimeAnimatorController = fx; anim.Rebind();
        void SetF(string n, float v) { var pr = anim.parameters.FirstOrDefault(x => x.name == n); if (pr == null) { o.Add("no param " + n); return; } if (pr.type == AnimatorControllerParameterType.Float) anim.SetFloat(n, v); else if (pr.type == AnimatorControllerParameterType.Bool) anim.SetBool(n, v > 0.5f); else anim.SetInteger(n, (int)v); }
        void Step(string label, int n) { for (int i = 0; i < n; i++) anim.Update(0.05f); o.Add($"sim {label}: anchor active={anchor.gameObject.activeInHierarchy} floorActive={floorT.gameObject.activeSelf} freeze={pc.FreezeToWorld} floor={floorT.localPosition.ToString("F2")} seat={floor.localPosition.ToString("F2")} visual={floor.Find("Visual").gameObject.activeSelf} kick={KickState(anim)}"); }
        SetF(PForward, 0.3f); SetF(PColor, 1);
        Step("start", 2);
        SetF(POn, 1); Step("On +0.05 (1回目)", 1); Step("On +0.3 (待ち)", 5); Step("On +0.65 (2回目)", 7); Step("On +1.0", 7); Step("On +2.0", 20); Step("On +3.0", 20);
        SetF(PHeight, 0.4f); Step("Height 0.4", 4);
        SetF(PHeight, 1f); Step("Height 1", 4);
        SetF(PColor, 0); Step("Color 0", 4);
        SetF(PForward, 1f); Step("Forward 1", 4);
        var st = anim.GetCurrentAnimatorStateInfo(fx.layers.ToList().FindIndex(l => l.name.Contains("MyAirPlatform")));
        SetF(PSlot, 1); Step("Replace 1 +0.05", 1); Step("Replace 1 +0.5", 9);
        SetF(PSlot, 0); Step("Replace 0 +0.05", 1); Step("Replace 0 +0.5", 9);
        SetF(POn, 0); Step("Off", 2);
        System.IO.File.WriteAllLines(Arg("-out") ?? "D:/Unity/backup/airplatform_verify.txt", o);
        UnityEngine.Object.DestroyImmediate(clone);
    }
}
