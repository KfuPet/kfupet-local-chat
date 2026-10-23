using System.Windows;
using kfupet_local_chat.Services;

namespace kfupet_local_chat
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // 必须在任何 LLamaSharp 调用之前注册，原生库只会加载一次
            LlamaLogCollector.Configure();
        }
    }

}
