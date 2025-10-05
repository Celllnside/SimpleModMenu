using UnityEngine;
using BepInEx.Logging;

namespace SimpleModMenu
{
    public class GuiController : MonoBehaviour
    {
        private static GuiController _instance;
        private bool _show;
        private Rect _panelRect = new Rect(60, 60, 600, 340);
        private readonly ManualLogSource _log = BepInEx.Logging.Logger.CreateLogSource(Core.PluginName + ".GUI");
        private bool _dragging;
        private Vector2 _dragOffset;
        private Vector2 _scrollPos;
        private bool _expandList;

        private const float LineH = 20f;
        private const float Pad = 6f;
        private GUIStyle _sectionHeader;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _log.LogInfo("GuiController ready (F6/F7 toggle)." );
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6) || Input.GetKeyDown(KeyCode.F7)) { _show = !_show; }
        }

        private void EnsureStyles()
        {
            if (_sectionHeader == null)
            {
                _sectionHeader = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }

        private void OnGUI()
        {
            if (!_show || Core.Instance == null) return;
            EnsureStyles();
            GUI.BeginGroup(_panelRect, GUI.skin.box);
            DrawTitleBar();
            float y = 28f; // below title
            var core = Core.Instance;

            // Section: Infinite Features
            y = SectionHeader("Infinite Features", y);
            float x = Pad;
            core.EnableInfinite = GUI.Toggle(new Rect(x, y, 150, LineH), core.EnableInfinite, "Infinite Refreshes"); x += 150;
            core.EnableInfiniteBanishes = GUI.Toggle(new Rect(x, y, 150, LineH), core.EnableInfiniteBanishes, "Infinite Banishes"); x += 150;
            core.EnableInfiniteSkips = GUI.Toggle(new Rect(x, y, 140, LineH), core.EnableInfiniteSkips, "Infinite Skips");
            y += LineH + 4;

            // Section: Forced Value
            y = SectionHeader("Forced Value", y);
            GUI.Label(new Rect(Pad, y, 90, LineH), "Count:");
            GUI.Label(new Rect(Pad + 50, y, 90, LineH), Core.ForcedRefreshValue.ToString());
            int[] steps = { -1000, -100, -10, -1, +1, +10, +100, +1000 };
            float bx = Pad + 120;
            foreach (int s in steps)
            {
                if (GUI.Button(new Rect(bx, y, 48, LineH), s > 0 ? "+" + s : s.ToString()))
                {
                    core.SetForcedRefreshValue(Core.ForcedRefreshValue + s);
                }
                bx += 50;
            }
            if (GUI.Button(new Rect(bx, y, 50, LineH), "Log")) _log.LogInfo($"Forced refresh value = {Core.ForcedRefreshValue}");
            y += LineH + 6;

            // Section: Resources
            y = SectionHeader("Resources", y);
            float rx = Pad;
            if (GUI.Button(new Rect(rx, y, 80, LineH), "+1k Gold")) core.TryAddGold(1000); rx += 82;
            if (GUI.Button(new Rect(rx, y, 80, LineH), "+10k")) core.TryAddGold(10_000); rx += 82;
            if (GUI.Button(new Rect(rx, y, 80, LineH), "+100k")) core.TryAddGold(100_000); rx += 82;
            if (GUI.Button(new Rect(rx, y, 80, LineH), "+1M")) core.TryAddGold(1_000_000); rx += 82;
            if (GUI.Button(new Rect(rx, y, 60, LineH), "+XP")) core.TryAddXp(500); rx += 62;
            if (GUI.Button(new Rect(rx, y, 70, LineH), "+5kXP")) core.TryAddXp(5_000); rx += 72;
            if (GUI.Button(new Rect(rx, y, 50, LineH), "+HP")) core.TryAddHealth(25);
            y += LineH + 6;

            // Section: Patching & Debug
            y = SectionHeader("Patching & Debug", y);
            core.AggressiveMode = GUI.Toggle(new Rect(Pad, y, 120, LineH), core.AggressiveMode, "Aggressive");
            core.DebugLogging = GUI.Toggle(new Rect(Pad + 130, y, 120, LineH), core.DebugLogging, "Debug Log");
            if (GUI.Button(new Rect(Pad + 260, y, 80, LineH), "Reapply")) core.ReapplyPatches();
            GUI.Label(new Rect(Pad + 350, y, 230, LineH), $"Forced: {Core.ForcedRefreshValue}");
            y += LineH + 2;
            GUI.Label(new Rect(Pad, y, 560, LineH), $"Patched -> Generic:{core.GenericPatchCount} EShop:{core.EShopItemPatchCount} Inv:{core.PlayerInventoryPatchCount}");
            y += LineH + 6;

            // Section: Patched Methods list (collapsible)
            y = SectionHeader("Patched Methods" + (_expandList ? " (click to collapse)" : " (click to expand)"), y, clickable:true, toggleAction:() => _expandList = !_expandList);
            if (_expandList)
            {
                float listTop = y;
                float listHeight = _panelRect.height - listTop - Pad;
                if (listHeight > 60)
                {
                    Rect viewRect = new Rect(0, 0, _panelRect.width - 32, Core.PatchedMethodNames.Count * 18 + 4);
                    Rect scrollRect = new Rect(Pad, listTop, _panelRect.width - 2 * Pad, listHeight);
                    _scrollPos = GUI.BeginScrollView(scrollRect, _scrollPos, viewRect);
                    float ly = 2f;
                    foreach (var name in Core.PatchedMethodNames)
                    {
                        GUI.Label(new Rect(0, ly, viewRect.width - 4, 18), name);
                        ly += 18f;
                    }
                    GUI.EndScrollView();
                }
            }

            GUI.EndGroup();
        }

        private float SectionHeader(string title, float y, bool clickable=false, System.Action toggleAction=null)
        {
            Rect r = new Rect(Pad, y, _panelRect.width - 2 * Pad, LineH);
            if (clickable && GUI.Button(r, title, _sectionHeader))
            {
                toggleAction?.Invoke();
            }
            else if(!clickable)
            {
                GUI.Label(r, title, _sectionHeader);
            }
            return y + LineH + 2f;
        }

        private void DrawTitleBar()
        {
            var bar = new Rect(0, 0, _panelRect.width, 24);
            GUI.Box(bar, "" );
            GUI.Label(new Rect(8, 2, _panelRect.width - 80, 20), "Mod Menu", GUI.skin.label);
            if (GUI.Button(new Rect(_panelRect.width - 60, 2, 24, 20), _expandList ? "-" : "+")) _expandList = !_expandList; // quick toggle methods
            if (GUI.Button(new Rect(_panelRect.width - 32, 2, 24, 20), "X")) _show = false;
            HandleDrag(bar);
        }

        private void HandleDrag(Rect localDragRect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && localDragRect.Contains(e.mousePosition)) { _dragging = true; _dragOffset = e.mousePosition; e.Use(); }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                Vector2 screenMouse = new Vector2(e.mousePosition.x + _panelRect.x, e.mousePosition.y + _panelRect.y);
                _panelRect.x = Mathf.Clamp(screenMouse.x - _dragOffset.x, 0, Screen.width - _panelRect.width);
                _panelRect.y = Mathf.Clamp(screenMouse.y - _dragOffset.y, 0, Screen.height - _panelRect.height);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0) _dragging = false;
        }
    }
}
