using System;
using System.Globalization;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.MainMenu;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build Skill Guide Book shots (KAN-64), next to <see cref="DevScreenshot"/>: <c>-ss-skill-guide &lt;spec&gt;</c>
    /// opens the GRIMOIRE page of the main menu once the scene is ready. Spec = comma list: <c>list</c> (default) or a
    /// skill id (opens its detail page), <c>kind=attack|defence|utility</c>, <c>tier=common|rare|epic|legendary</c>,
    /// <c>hide</c> (unfound skills as "???"), <c>scroll=&lt;px&gt;</c> (scroll the page that shows). Logs
    /// <c>[SkillGuideShotArgs] ...</c> with the card count and the detail rows. Pair with <c>-ss-scene MainMenu</c>.
    /// Dev builds only; no effect without the arg.
    /// </summary>
    public sealed class SkillGuideShotArgs : MonoBehaviour
    {
        private string _spec = "list";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string spec = GetArg("-ss-skill-guide");
            if (string.IsNullOrEmpty(spec)) return;
            var go = new GameObject(nameof(SkillGuideShotArgs));
            DontDestroyOnLoad(go);
            var args = go.AddComponent<SkillGuideShotArgs>();
            args._spec = spec;
            DevScreenshot.SceneReady += args.Open;
            DevScreenshot.BeforeCapture += args.Log;
        }

        private void OnDestroy()
        {
            DevScreenshot.SceneReady -= Open;
            DevScreenshot.BeforeCapture -= Log;
        }

        private void Open()
        {
            var menu = FindAnyObjectByType<MainMenuController>();
            if (menu == null || menu.GuidePage == null)
            {
                Debug.LogWarning("[SkillGuideShotArgs] -ss-skill-guide: no main menu with a Skill Guide Book in the scene");
                return;
            }
            var page = menu.GuidePage;
            menu.OpenGuide();
            string detail = null;
            float scroll = -1f;
            foreach (var raw in _spec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (part == "list") continue;
                if (part == "hide") page.HideUnfound = true;
                else if (part.StartsWith("kind=", StringComparison.Ordinal) && Enum.TryParse(part.Substring(5), true, out SkillKind kind))
                    page.SetKindFilter(kind);
                else if (part.StartsWith("tier=", StringComparison.Ordinal) && Enum.TryParse(part.Substring(5), true, out SkillRarity tier))
                    page.SetTierFilter(tier);
                else if (part.StartsWith("scroll=", StringComparison.Ordinal))
                    float.TryParse(part.Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out scroll);
                else detail = part;
            }
            if (detail != null && !page.ShowDetail(detail))
                Debug.LogWarning($"[SkillGuideShotArgs] unknown or hidden skill '{detail}'");
            if (scroll >= 0f)
            {
                float y = scroll;
                page.Page.schedule.Execute(() => page.ScrollTo(y)).StartingIn(300); // after the first layout pass
            }
            Log();
        }

        private void Log()
        {
            var page = FindAnyObjectByType<MainMenuController>()?.GuidePage;
            if (page == null) return;
            string detail = page.DetailSkill != null
                ? $" detail={page.DetailSkill.Id} rows={page.LevelRowCount} row1='{page.LevelRowText(1)}' last='{page.LevelRowText(page.LevelRowCount)}'"
                : "";
            Debug.Log($"[SkillGuideShotArgs] open={page.IsOpen} skills={page.Skills.Count} visible={page.VisibleSkills.Count} " +
                      $"kind={page.KindFilter?.ToString() ?? "all"} tier={page.TierFilter?.ToString() ?? "all"} hide={page.HideUnfound}{detail}");
        }

        private static string GetArg(string name)
        {
            var args = DevArgs.All; // KAN-179: command line or SS_ARGS (iOS)
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
