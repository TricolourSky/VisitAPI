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
    /// <summary>
    /// 1.1 独有：按季节开关一组物件（Skier / Jaeger 房里各有 `SUMMER`（夏/秋/春）和 `WINTER`（冬）两组，场景文件里两组都是激活的，
    /// 1.1 运行时靠这个组件按当前季节只留一组）。0.16 没有这个类，打包时一度被剥掉，两组一起显示 → Skier 房「在下雪」（09-06 SORA 实机）。
    /// bundle 里的组件按 (VisitAPI 程序集, EFT, SeasonEnvironment) 绑到这里；SDK 桩 IsolatedSDK\…\_VisitStubs\SeasonEnvironment.cs（同 guid，字段 byte[]）。
    /// 字段与 1.1 逐字一致：`public ESeason[] EnabledOnSeasons`（ESeason 是 byte 枚举，1.1 与 0.16 的值表相同：Summer0 Autumn1 Winter2 Spring3 AutumnLate4 SpringEarly5）。
    /// </summary>
    public class SeasonEnvironment : MonoBehaviour
    {
        public ESeason[] EnabledOnSeasons;

        public bool IsMatch(ESeason season) => EnabledOnSeasons != null && Array.IndexOf(EnabledOnSeasons, season) >= 0;
    }
}

namespace VisitAPI.Native
{
    /// <summary>访问就位后按当前季节把房间里的 `SeasonEnvironment` 组件各自开关一遍（1.1 那个类在 0.16 里没人调，得由我们来调）。
    /// 当前季节：有 SeasonsController（战局里）就用它；商人访问里没有，按 SPT 服务端 `/client/weather` 报的季节走（SPT 自己按日期表算的那个，
    /// 09-06 是夏季）。取不到就当夏季——1.1 正式版的截图也是夏季。</summary>
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
            catch (Exception) { /* 访问里没有战局世界，正常 */ }
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
