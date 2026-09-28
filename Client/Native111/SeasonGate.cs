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
            foreach (var root in scene.GetRootGameObjects())
                foreach (var env in root.GetComponentsInChildren<SeasonEnvironment>(true))
                {
                    var match = env.IsMatch(season);
                    if (env.gameObject.activeSelf != match) env.gameObject.SetActive(match);
                }
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
                    return _season.Value;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[narrate] could not get the SPT season, assuming summer: " + e.Message); }
            _season = ESeason.Summer;
            return ESeason.Summer;
        }
    }
}
