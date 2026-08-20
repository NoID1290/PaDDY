namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Specifies the underlying audio output engine backend.
    /// </summary>
    public enum AudioEngineType
    {
        /// <summary>
        /// NAudio WASAPI (Windows Audio Session API) shared mode backend.
        /// </summary>
        NAudio = 0,

        /// <summary>
        /// ManagedBass (Un4seen BASS 2.4) hardware/direct output audio engine backend.
        /// </summary>
        ManagedBass = 1
    }
}
