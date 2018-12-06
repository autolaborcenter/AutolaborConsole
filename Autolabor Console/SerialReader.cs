using System;
using System.IO.Ports;
using System.Linq;
using System.Text;

namespace Autolabor_Console {
	public static class Functions {
		public static U Let<T, U>(this T receiver, Func<T, U> block) => block(receiver);

		public static T Also<T>(this T receiver, Action<T> block) {
			block(receiver);
			return receiver;
		}

		/// <summary>
		///     按范围拷贝数组
		/// </summary>
		/// <param name="receiver">原数组</param>
		/// <param name="begin">起始坐标（包括）</param>
		/// <param name="end">结束坐标（不包括）</param>
		/// <typeparam name="T">数组类型，引用类型浅拷贝</typeparam>
		/// <returns>新数组</returns>
		public static T[] CopyRange<T>(
			this T[] receiver,
			int      begin = 0,
			int      end   = int.MaxValue
		) => new T[Math.Min(end, receiver.Length) - begin]
		   .Also(it => Array.Copy(receiver, begin, it, 0, it.Length));
	}


	public struct Command {
		public readonly byte   Id;
		public readonly byte   Seq;
		public readonly byte[] Payload;

		public Command(byte id, byte seq, byte[] payload) {
			Id      = id;
			Seq     = seq;
			Payload = payload;
		}

		public override string ToString() {
			var builder = new StringBuilder();
			builder.Append($"Id: {Id}, Seq: {Seq}, Payload: ");
			foreach (var b in Payload) {
				builder.Append(Convert.ToString(b, 16));
				builder.Append(' ');
			}

			return builder.ToString();
		}
	}


	public class SerialReader {
		private          short  _state;
		private          byte[] _buffer;
		private readonly Object _lock = new object();


		public (bool, Command?) Read(SerialPort port) {
			lock (_lock) {
				while (true) {
					var @int = port.ReadByte();
					if (@int == -1) return (false, null);

					var @byte = (byte) @int;
					switch (_state) {
						case 0:
							if (@byte == 0x55) ++_state;
							break;
						case 1:
							if (@byte == 0xAA) ++_state;
							break;
						case 2:
							if (@byte != 2 && @byte != 9) {
								_state = 0;
								break;
							}

							_buffer    = new byte[@byte + 5];
							_buffer[0] = 0x55;
							_buffer[1] = 0xAA;
							_buffer[2] = @byte;
							++_state;
							break;
						default:
							_buffer[_state++] = @byte;
							if (_state != _buffer.Length) break;
							_state = 0;
							return _buffer
							      .Take(_buffer.Length - 1)
							      .Aggregate((tail, it) => (byte) (tail ^ it))
							      .Let(it => it == @byte)
								       ? (true, new Command(_buffer[4],
								                            _buffer[3],
								                            _buffer.CopyRange(5, _buffer.Length - 1)))
								       : (false, new Command(0, 0, _buffer));
					}
				}
			}
		}
	}
}