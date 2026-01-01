using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Xml.Linq;
using Audio;

public class CraftingSkillEditor : IModApi
{
    public void InitMod(Mod mod)
    {
        if (GameManager.IsDedicatedServer) return;
        var go = new GameObject("CraftingSkillEditor_Bootstrap");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<CraftingSkillEditorBootstrap>();
    }
}

static class CSEConfig
{
    public static KeyCode ToggleKey { get; private set; } = KeyCode.F8;
    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;

        try
        {
            string path = Path.Combine(
                GameIO.GetGameDir(""),
                "Mods",
                "CraftingSkillEditor",
                "Config",
                "KeyBinding.xml"
            );

            if (!File.Exists(path)) return;

            var doc = XDocument.Load(path);
            var node = doc.Root?.Element("ToggleKey");
            if (node == null) return;

            if (Enum.TryParse(node.Value.Trim(), true, out KeyCode key))
                ToggleKey = key;
        }
        catch { }
    }
}

public class CraftingSkillEditorBootstrap : MonoBehaviour
{
    CraftingSkillEditorUI ui;
    KeyCode toggleKey = KeyCode.None;

    void Update()
    {
        if (GameManager.IsDedicatedServer) return;

        var p = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
        if (p == null || !p.bSpawned || CraftingSkillEditorUI.IsAnyInputFocused) return;

        if (toggleKey == KeyCode.None)
        {
            CSEConfig.Load();
            toggleKey = CSEConfig.ToggleKey;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            if (ui == null)
            {
                ui = gameObject.AddComponent<CraftingSkillEditorUI>();
                ui.Build();
            }
            ui.Toggle();
        }
    }
}

public class CraftingSkillEditorUI : MonoBehaviour
{
    public static bool IsAnyInputFocused;

    GameObject root;
    RectTransform content;
    InputField searchField;
    readonly List<Row> rows = new List<Row>();

    class Row
    {
        public string name;
        public int current, max;
        public Text currentText;
        public InputField input;
        public Image bg;
        public Button apply;
        public Text applyLabel;
        public GameObject root;
    }

    public void Build()
    {
        if (root != null) return;

        root = new GameObject("CSE_UI");
        DontDestroyOnLoad(root);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();

        var panel = UI("Panel", root.transform);
        var prt = panel.AddComponent<RectTransform>();
        prt.sizeDelta = new Vector2(900, 680);
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        panel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.15f, 0.97f);

        var title = Text(UI("Title", panel.transform), "Crafting Skill Editor", 18, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        SetRectWithInset(title.rectTransform, 0.02f, 0.55f, 0.94f, 0.99f);

        CreateAction(panel.transform, "Max All", 0.48f, MaxAll);
        CreateAction(panel.transform, "Zero All", 0.60f, ZeroAll);
        CreateAction(panel.transform, "Reset All", 0.72f, ResetAll);
        CreateAction(panel.transform, "Apply All", 0.84f, ApplyAll);

        searchField = CreateSearchInput(panel.transform, "");
        searchField.onValueChanged.AddListener(FilterRows);
        SetRect(searchField.GetComponent<RectTransform>(), 0.02f, 0.30f, 0.88f, 0.93f);

        var header = UI("Header", panel.transform);
        SetRect(header.AddComponent<RectTransform>(), 0.02f, 0.98f, 0.84f, 0.89f);
        header.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f);

        HeaderText(header, "Skill", 0f, 0.45f, true);
        HeaderText(header, "Current", 0.48f, 0.58f);
        HeaderText(header, "Set", 0.60f, 0.72f);
        HeaderText(header, "Max", 0.74f, 0.82f);
        HeaderText(header, "Apply", 0.84f, 0.96f);

        var scrollGO = UI("Scroll", panel.transform);
        SetRect(scrollGO.AddComponent<RectTransform>(), 0.02f, 0.98f, 0.05f, 0.83f);

        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.vertical = true;
        scroll.scrollSensitivity = 45f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var viewport = UI("Viewport", scrollGO.transform);
        SetRect(viewport.AddComponent<RectTransform>(), 0f, 1f, 0f, 1f);
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.02f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var contentGO = UI("Content", viewport.transform);
        content = contentGO.AddComponent<RectTransform>();
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.childForceExpandHeight = false;
        vlg.spacing = 6;
        vlg.padding = new RectOffset(64, 8, 8, 8);

        contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content;

        Refresh();
        root.SetActive(false);
    }

    public void Toggle() => root.SetActive(!root.activeSelf);

    void Update()
    {
        IsAnyInputFocused = searchField != null && searchField.isFocused;
        if (IsAnyInputFocused) return;

        foreach (var r in rows)
            if (r.input != null && r.input.isFocused)
            {
                IsAnyInputFocused = true;
                break;
            }
    }

    void Refresh()
    {
        foreach (Transform c in content) Destroy(c.gameObject);
        rows.Clear();

        var p = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
        if (p == null) return;

        foreach (var pv in p.Progression.ProgressionValueQuickList)
        {
            if (pv?.ProgressionClass == null) continue;
            var skill = pv.ProgressionClass.Name;
            if (!skill.StartsWith("crafting", StringComparison.OrdinalIgnoreCase)) continue;

            var rowGO = UI(skill, content);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 44;

            var bg = rowGO.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.16f, 0.2f);

            var nameText = Text(UI("Name", rowGO.transform), Pretty(skill), 16, TextAnchor.MiddleLeft);
            nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetRectWithInset(nameText.rectTransform, 0f, 0.45f, 0f, 1f);

            var curText = Text(UI("Current", rowGO.transform), pv.Level.ToString(), 16, TextAnchor.MiddleCenter);
            SetRect(curText.rectTransform, 0.48f, 0.58f, 0f, 1f);

            var input = CreateNumberInput(rowGO.transform, pv.Level.ToString());
            SetRect(input.GetComponent<RectTransform>(), 0.60f, 0.72f, 0.15f, 0.85f);

            var maxText = Text(UI("Max", rowGO.transform), pv.ProgressionClass.MaxLevel.ToString(), 14, TextAnchor.MiddleCenter);
            SetRect(maxText.rectTransform, 0.74f, 0.82f, 0f, 1f);

            var apply = Button(UI("Apply", rowGO.transform), "Apply");
            SetRect(apply.GetComponent<RectTransform>(), 0.84f, 0.96f, 0.15f, 0.85f);

            var row = new Row
            {
                name = skill,
                current = pv.Level,
                max = pv.ProgressionClass.MaxLevel,
                currentText = curText,
                input = input,
                bg = bg,
                apply = apply,
                applyLabel = apply.GetComponentInChildren<Text>(),
                root = rowGO
            };

            apply.onClick.AddListener(() => ApplyRow(row));
            input.onValueChanged.AddListener(_ => UpdateDelta(row));
            rows.Add(row);
        }

        if (!string.IsNullOrEmpty(searchField.text))
            FilterRows(searchField.text);
    }

    void MaxAll() { foreach (var r in rows) r.input.text = r.max.ToString(); }
    void ZeroAll() { foreach (var r in rows) r.input.text = "1"; }
    void ResetAll() { foreach (var r in rows) r.input.text = r.current.ToString(); }
    void ApplyAll() { foreach (var r in rows) if (r.input.text != r.current.ToString()) ApplyRow(r); }

    void FilterRows(string txt)
    {
        foreach (var r in rows)
            r.root.SetActive(string.IsNullOrEmpty(txt) ||
                Pretty(r.name).IndexOf(txt, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    void ApplyRow(Row r)
    {
        var p = GameManager.Instance.World.GetPrimaryPlayer() as EntityPlayerLocal;
        if (!int.TryParse(r.input.text, out int v)) return;

        v = Mathf.Clamp(v, 0, r.max);
        p.Progression.GetProgressionValue(r.name).Level = v;

        r.current = v;
        r.currentText.text = v.ToString();
        r.input.text = v.ToString();
        UpdateDelta(r);

        p.Progression.bProgressionStatsChanged = true;
        p.bPlayerStatsChanged = true;
        Manager.PlayInsidePlayerHead("ui_notification", p.entityId);
    }

    void UpdateDelta(Row r)
    {
        if (!int.TryParse(r.input.text, out int v)) return;
        bool dirty = v != r.current;
        r.bg.color = dirty ? (v > r.current ? new Color(0.2f, 0.35f, 0.2f) : new Color(0.35f, 0.2f, 0.2f)) : new Color(0.16f, 0.16f, 0.2f);
        r.applyLabel.text = dirty ? "Apply*" : "Apply";
    }

    static void SetRectWithInset(RectTransform rt, float xmin, float xmax, float ymin, float ymax)
    {
        float inset = Mathf.Max(8f, Screen.width * 0.004f);
        rt.anchorMin = new Vector2(xmin, ymin);
        rt.anchorMax = new Vector2(xmax, ymax);
        rt.offsetMin = new Vector2(inset, 0f);
        rt.offsetMax = Vector2.zero;
    }

    static void SetRect(RectTransform rt, float xmin, float xmax, float ymin, float ymax)
    {
        rt.anchorMin = new Vector2(xmin, ymin);
        rt.anchorMax = new Vector2(xmax, ymax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void CreateAction(Transform parent, string txt, float minX, Action act)
    {
        var b = Button(UI(txt, parent), txt);
        SetRect(b.GetComponent<RectTransform>(), minX, minX + 0.12f, 0.93f, 0.99f);
        b.onClick.AddListener(() => act());
    }

    static void HeaderText(GameObject parent, string txt, float xmin, float xmax, bool left = false)
    {
        var t = Text(UI(txt, parent.transform), txt, 14, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
        if (left) SetRectWithInset(t.rectTransform, xmin, xmax, 0f, 1f);
        else SetRect(t.rectTransform, xmin, xmax, 0f, 1f);
    }

    static string Pretty(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        if (s.StartsWith("crafting", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(8);

        switch (s.ToLowerInvariant())
        {
            case "harvestingtools": return "Harvesting Tools";
            case "repairtools": return "Repair Tools";
            case "salvagetools": return "Salvage Tools";
            case "sledgehammers": return "Sledge Hammers";
            case "machineguns": return "Machine Guns";
            case "workstations": return "Work Stations";
            case "handguns": return "Handguns";
            case "shotguns": return "Shotguns";
            case "rifles": return "Rifles";
            case "bows": return "Bows";
            case "spears": return "Spears";
            case "clubs": return "Clubs";
            case "blades": return "Blades";
            case "knuckles": return "Knuckles";
            case "explosives": return "Explosives";
            case "robotics": return "Robotics";
            case "vehicles": return "Vehicles";
            case "electrician": return "Electrician";
            case "medical": return "Medical";
            case "food": return "Food";
            case "seeds": return "Seeds";
            case "traps": return "Traps";
            case "armor": return "Armor";
        }

        return char.ToUpper(s[0]) + s.Substring(1);
    }

    static GameObject UI(string n, Transform p)
    {
        var g = new GameObject(n);
        g.transform.SetParent(p, false);
        return g;
    }

    static Text Text(GameObject g, string t, int s, TextAnchor a)
    {
        var tx = g.AddComponent<Text>();
        tx.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        tx.text = t;
        tx.fontSize = s;
        tx.alignment = a;
        tx.color = Color.white;
        tx.raycastTarget = false;
        return tx;
    }

    static Button Button(GameObject g, string t)
    {
        g.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f);
        var b = g.AddComponent<Button>();
        Text(UI("T", g.transform), t, 14, TextAnchor.MiddleCenter).raycastTarget = false;
        return b;
    }

    static InputField CreateNumberInput(Transform p, string val)
    {
        var g = UI("Input", p);
        g.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f);
        var i = g.AddComponent<InputField>();
        i.contentType = InputField.ContentType.IntegerNumber;
        var t = Text(UI("Text", g.transform), val, 14, TextAnchor.MiddleLeft);
        i.textComponent = t;
        i.text = val;
        return i;
    }

    static InputField CreateSearchInput(Transform p, string val)
    {
        var g = UI("Search", p);
        g.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.22f);
        var i = g.AddComponent<InputField>();
        i.contentType = InputField.ContentType.Standard;
        var t = Text(UI("Text", g.transform), val, 14, TextAnchor.MiddleLeft);
        var ph = Text(UI("Placeholder", g.transform), "Search…", 14, TextAnchor.MiddleLeft);
        ph.color = new Color(0.7f, 0.7f, 0.75f, 0.75f);
        i.textComponent = t;
        i.placeholder = ph;
        i.text = val;
        return i;
    }
}
