using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Autolabor_Console {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow {
        private byte _sequence; // 发送指令序列号
        private long _taskSeq; // 速度控制协程序列号

        // 处理指令的状态机
        private readonly SerialReader _reader = new SerialReader();

        // 串口
        private readonly SerialPort _serial
            = new SerialPort {
                PortName = "COM1",
                BaudRate = 115200
            };

        public MainWindow() {
            InitializeComponent();

            _serial.DataReceived +=
                (sender, e) => {
                    var (ok, command) = _reader.Read((SerialPort) sender);
                    if (ok) {
                        var payload = command.Value.Payload;
                        switch (command.Value.Id) {
                            case 1: // 解析编码器
                                var left = payload[0] << 8 | payload[1];
                                var right = payload[2] << 8 | payload[3];
                                Dispatcher.Invoke(() => EncoderBlock.Text = $"左轮：{left}  |  右轮：{right}");
                                break;
                            case 2: // 解析电量
                                Dispatcher.Invoke(() => BatteryBlock.Text = payload[0].ToString());
                                break;
                            case 0xFF: // 超时，重置状态
                                Send(new byte[] {5, 0});
                                break;
                        }

                        Dispatcher.Invoke(() => UpdateRespond(command.Value.ToString()));
                    }
                    else if (command.HasValue)
                        Dispatcher.Invoke(() => UpdateRespond("received but check failed"));
                };

            Scan();
        }

        /// <summary>
        /// 	更新返回列表
        /// </summary>
        /// <param name="text">内容</param>
        private void UpdateRespond(string text)
            => Dispatcher.Invoke
            (() => {
                lock (RespondView) {
                    if (RespondView.Items.Count > 20)
                        RespondView.Items.RemoveAt(0);
                    RespondView.Items.Add(text);
                }
            });

        /// <summary>
        /// 	扫描串口
        /// </summary>
        private void Scan()
            => Dispatcher.Invoke
            (() => {
                lock (SerialsBox) {
                    SerialsBox.Items.Clear();
                    foreach (var port in SerialPort.GetPortNames())
                        SerialsBox.Items.Add(port);

                    if (SerialsBox.Items.Count > 0)
                        SerialsBox.SelectedIndex = 0;
                }
            });

        /// <summary>
        /// 	发送一条指令
        /// </summary>
        /// <param name="payload">指令内容</param>
        private void Send(byte[] payload) {
            Debug.Assert(payload.Length < 256);
            lock (_serial) {
                if (!_serial.IsOpen) return;
                var stream = new MemoryStream(5 + payload.Length);
                stream.WriteByte(0x55);
                stream.WriteByte(0xAA);
                stream.WriteByte((byte) payload.Length);
                stream.WriteByte(_sequence++);
                stream.Write(payload, 0, payload.Length);

                stream.WriteByte(stream
                    .GetBuffer()
                    .Take(stream.Capacity - 1)
                    .Aggregate((tail, it) => (byte) (tail ^ it)));

                _serial.Write(stream.GetBuffer(), 0, stream.Capacity);
                Thread.Sleep(20);
            }
        }

        private void Scan_Click(object sender, RoutedEventArgs e) => Scan();

        /// <summary>
        /// 	开始一个协程，每秒询问一次电量
        /// </summary>
        private async Task RequireBattery() {
            while (_serial.IsOpen) {
                await Task.Delay(1000);
                Send(new byte[] {2, 0});
            }
        }

        /// <summary>
        /// 	开始一个协程，每100毫秒询问一次编码器
        /// </summary>
        private (int, int) Velocity {
            set {
                var seq = Interlocked.Increment(ref _taskSeq);
                var v = 8 * (VelocityBox.SelectedIndex + 1);

                var (left, right) = value;
                left *= v;
                right *= v;

                Task.Run(async () => {
                    while (_serial.IsOpen && seq == _taskSeq) {
                        await Task.Delay(100);
                        Send(new byte[] {
                            1,
                            (byte) (left >> 8), (byte) left,
                            (byte) (right >> 8), (byte) right,
                            0, 0, 0, 0
                        });
                    }
                });
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e) {
            lock (_serial) {
                if (_serial.IsOpen) {
                    _serial.Close();
                    ((Button) sender).Content = "打开";
                }
                else if (SerialsBox.SelectedItem != null) {
                    _serial.PortName = (string) SerialsBox.SelectedItem;
                    try {
                        _serial.Open();

                        Task.Run(RequireBattery);
                        Velocity = (0, 0);

                        ((Button) sender).Content = "关闭";
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
        }

        private void Clear_OnClick(object sender, RoutedEventArgs e) {
            lock (RespondView) {
                RespondView.Items.Clear();
            }
        }

        private void Drive_OnClick(object sender, RoutedEventArgs e) {
            if (!_serial.IsOpen) return;
            var value = ((sender as Control)?.Tag as string)?.Split(' ') ?? throw new Exception();
            Velocity = (Convert.ToInt16(value[0]), Convert.ToInt16(value[1]));
        }

        private void ClearEncoder_OnClick(object sender, RoutedEventArgs e) {
            Send(new byte[] {6, 0});
            Dispatcher.Invoke(() => EncoderBlock.Text = "(0, 0)");
        }
    }
}