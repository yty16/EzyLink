using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiTongYao.Core;

namespace YiTongYao.Windows
{
    /// <summary>扫码连接窗口。</summary>
    public partial class QrWindow : Window
    {
        private readonly string _payload;

        public QrWindow(string ip, int port, string pairCode, string btMac = "")
        {
            InitializeComponent();
            _payload = QrEncoder.BuildPayload(ip, port, pairCode, btMac);
            AddrText.Text = ip + ":" + port;

            try
            {
                bool[][] matrix = QrEncoder.EncodeWithQuietZone(_payload);
                int scale = Math.Max(4, 320 / matrix.Length);
                QrImage.Source = Render(matrix, scale);
            }
            catch (Exception ex)
            {
                AddrText.Text = "二维码生成失败：" + ex.Message;
            }
        }

        /// <summary>把模块矩阵渲染成位图，scale 为每个模块的像素边长。</summary>
        private static BitmapSource Render(bool[][] matrix, int scale)
        {
            int n = matrix.Length;
            int px = n * scale;
            var bmp = new WriteableBitmap(px, px, 96, 96, PixelFormats.Bgr24, null);
            var pixels = new byte[px * px * 3];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    bool dark = matrix[r][c];
                    byte bb = dark ? (byte)0x36 : (byte)0xFF;   // B
                    byte gg = dark ? (byte)0x11 : (byte)0xFF;   // G
                    byte rr = dark ? (byte)0x1B : (byte)0xFF;   // R
                    for (int dy = 0; dy < scale; dy++)
                    {
                        int rowOffset = ((r * scale + dy) * px + c * scale) * 3;
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int i = rowOffset + dx * 3;
                            pixels[i] = bb;
                            pixels[i + 1] = gg;
                            pixels[i + 2] = rr;
                        }
                    }
                }
            }
            int stride = px * 3;
            bmp.WritePixels(new Int32Rect(0, 0, px, px), pixels, stride, 0);
            return bmp;
        }

        private void CopyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(_payload);
                AddrText.Text = "已复制连接信息到剪贴板";
            }
            catch (Exception ex)
            {
                AddrText.Text = "复制失败：" + ex.Message;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
