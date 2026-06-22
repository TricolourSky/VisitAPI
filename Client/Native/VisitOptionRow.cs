using System;
using UnityEngine;

namespace VisitAPI.Native
{
    internal sealed class VisitOptionRow : MonoBehaviour
    {
        internal Action? OnClick;

        internal void Fire()
        {
            try { OnClick?.Invoke(); }
            catch (Exception ex) { Plugin.Log.LogError("[VisitOptionRow] " + ex); }
        }
    }
}
