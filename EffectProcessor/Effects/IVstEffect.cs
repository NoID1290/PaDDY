using System;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public struct VstParameterInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Display { get; set; }
        public string Label { get; set; }
        public float Value { get; set; }
    }

    public interface IVstEffect : IAudioEffect
    {
        bool HasEditor { get; }
        void OpenEditor(IntPtr hWnd);
        void CloseEditor();
        bool GetEditorSize(out int width, out int height);
        int GetParameterCount();
        VstParameterInfo GetParameterInfo(int index);
        void SetParameterValue(int index, float value);
    }
}
