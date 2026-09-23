using System.Collections.Generic;
using UnityEngine;

namespace EFT.Hair
{
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
                if (light == null) return;
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
