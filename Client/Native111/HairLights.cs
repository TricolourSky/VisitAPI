using System.Collections.Generic;
using UnityEngine;

namespace EFT.Hair
{
    /// <summary>
    /// 1.1 独有：商人头发的「专用打光」。1.1 的头发 shader `Characters/TraiderHair` 不吃场景常规灯，它的高光/层次
    /// 全靠 3 个属性 `_HairLightCount` / `_HairLightColor[4]` / `_HairLightDirection[4]`——由头部渲染器上的这个组件
    /// 每帧从场景里指定的几盏灯（`light_for_hair1/2`、`Character_FILL` 之类，有的灯 cullingMask=0，本来就只为头发存在）
    /// 抄进 MaterialPropertyBlock。0.16 没有这个类，打包时被剥掉 → 所有不戴帽子的商人头发一团死黑、没有层次（09-07 SORA 实机）。
    ///
    /// 逻辑是从 1.1 的 GameAssembly.dll（HairLights.Update，RVA 0x32D21E0）反汇编逐句抄的，不是猜的：
    ///   count = min(_lights.Count, 4)；对第 i 盏：
    ///     color[i]     = (light.color.rgb × intensity, light.range)
    ///     direction[i] = 方向光 ? (transform.rotation.eulerAngles, 1) : (transform.position, 0)
    ///   renderer.GetPropertyBlock → SetInt / SetVectorArray ×2 → SetPropertyBlock。
    /// 桩：IsolatedSDK\…\_VisitStubs\HairLights.cs（guid 36755ac6fd3d65f25843e07eaad70cc2 = 1.1 原脚本 EFT/Hair/HairLights.cs）。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class HairLights : MonoBehaviour
    {
        static readonly int _hairLightCount = Shader.PropertyToID("_HairLightCount");
        static readonly int _hairLightColor = Shader.PropertyToID("_HairLightColor");
        static readonly int _hairLightDirection = Shader.PropertyToID("_HairLightDirection");

        Vector4[] _colors;
        Vector4[] _directions;
        MaterialPropertyBlock _propertyBlock;
        Renderer _renderer;

#pragma warning disable 0649 // 从 bundle 反序列化
        [SerializeField] List<Light> _lights;
#pragma warning restore 0649

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _colors = new Vector4[4];
            _directions = new Vector4[4];
        }

        void Update()
        {
            if (_lights == null || _renderer == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();
            var count = Mathf.Min(_lights.Count, 4);
            for (var i = 0; i < count; i++)
            {
                var light = _lights[i];
                if (light == null) return;   // 1.1 原逻辑在这里会 NRE 整帧作废；这里等价地跳过本帧
                var c = light.color;
                var intensity = light.intensity;
                var t = light.transform;
                _directions[i] = light.type == LightType.Directional
                    ? new Vector4(t.rotation.eulerAngles.x, t.rotation.eulerAngles.y, t.rotation.eulerAngles.z, 1f)
                    : new Vector4(t.position.x, t.position.y, t.position.z, 0f);
                _colors[i] = new Vector4(c.r * intensity, c.g * intensity, c.b * intensity, light.range);
            }
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetInt(_hairLightCount, count);
            _propertyBlock.SetVectorArray(_hairLightColor, _colors);
            _propertyBlock.SetVectorArray(_hairLightDirection, _directions);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
