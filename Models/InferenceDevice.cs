namespace kfupet_local_chat.Models
{
    /// <summary>本地推理设备选择。</summary>
    public enum InferenceDevice
    {
        /// <summary>按显存自动判断是否使用 GPU。</summary>
        Auto = 0,

        /// <summary>强制使用 GPU 加速。</summary>
        Gpu = 1,

        /// <summary>强制使用 CPU。</summary>
        Cpu = 2
    }
}
