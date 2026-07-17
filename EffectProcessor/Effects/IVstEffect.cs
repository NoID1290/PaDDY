using System;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public interface IVstEffect : IAudioEffect
    {
        void OpenEditor(IntPtr hWnd);
        void CloseEditor();
    }
}
