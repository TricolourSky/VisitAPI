using System;
using System.Linq;
using Comfort.Common;
using EFT;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EFT
{
    public class SeasonEnvironment : MonoBehaviour
    {
        public ESeason[] EnabledOnSeasons;

        public bool IsMatch(ESeason season) => EnabledOnSeasons != null && Array.IndexOf(EnabledOnSeasons, season) >= 0;
    }
}

namespace VisitAPI.Native
{
    public static class SeasonGate
    {
        static ESeason? _season;

        public static void Apply(Scene scene)
        {
            if (!scene.isLoaded) return;
            var season = Current();
            var on = 0; var off = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var env in root.GetComponentsInChildren<SeasonEnvironment>(true))
                {
                    var match = env.IsMatch(season);
                    if (env.gameObject.activeSelf != match) env.gameObject.SetActive(match);
                    if (match) on++; else off++;
                }
            if (on + off > 0) Plugin.Log.LogInfo($"[narrate] 季节开关: '{scene.name}' 当前={season}，留 {on} 组、关 {off} 组");
        }

        static ESeason Current()
        {
            if (_season.HasValue) return _season.Value;
            try
            {
                var controller = Seasons.Controller;
                if (controller != null) { _season = controller.Season; return _season.Value; }
            }
            catch (Exception) {  }
            try
            {
                var json = RequestHandler.GetJson("/client/weather");
                var token = JObject.Parse(json)?["data"]?["season"];
                if (token != null && token.Type != JTokenType.Null)
                {
                    _season = (ESeason)(byte)token.Value<int>();
                    Plugin.Log.LogInfo($"[narrate] SPT 服务端季节: {_season}");
                    return _season.Value;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[narrate] 取不到 SPT 季节，按夏季处理: " + e.Message); }
            _season = ESeason.Summer;
            return ESeason.Summer;
        }
    }
}
