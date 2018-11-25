using System;
using System.Text.RegularExpressions;
using System.IO.Ports;
using System.Management;
using System.Collections;
using System.Collections.Generic;

namespace A10Cyclone
{
	///------------------------------------------------
	///A10CycloneClassについて
	///------------------------------------------------
	///A10Cyclone を動作させるクラス
	//詳しくは以下のURLを参照
	//http://www.rends.jp/en/index.php
	//http://nasu.bbspink.com/test/read.cgi/onatech/1417972897/
	
	public class A10CycloneClass
	{
		/// <summary>
		/// 機器情報
		/// </summary>
		public enum Model
		{
			A10Cyclone = 1,	// A10サイクロン.
			UFOSA = 2,		// UFO SA
			Unknown = 0xFF	// 不明
		};

		public enum Pattern
		{
			ClockWise = 0,	// 正転
			CounterClockWise	// 逆転
		}

		public const Int32 Level_Max = 127;
		public const Int32 Level_Min = 0;
		public const Int32 Level_Stop = Level_Min;

		public const Int32 Offset_CounterClockWise = 128;   // 逆転時に加算するデータ

		/// <summary>
		/// 機種判別用コマンド (A10サイクロン、 UFO SA共通)
		/// </summary>
		private readonly Byte[] ModelCheckCmd = new Byte[] { 0xF0, 0x01, 0x00 };

		/// 現在のパターン
		private Pattern _pattern = Pattern.ClockWise;
		public Pattern pattern
		{
			get { return _pattern; }
			set
			{
				//値だけ更新をするので注意
				_pattern = value;
				//振動の更新
				StatusUpDate();
			}
		}
		/// 現在のレベル
		private Int32 _level = Level_Stop;
		public Int32 level
		{
			get { return _level; }
			set
			{
				//値だけ更新をするので注意
				_level = Clamp(value, Level_Min, Level_Max);
				//振動の更新
				StatusUpDate();
			}
		}
		//Deviceの取得ができているかどうか
		private Boolean _DeviceEnable = false;
		public Boolean IsDeviceEnable
		{
			get { return _DeviceEnable; }
		}
		//ポーズの状態を取得
		private Boolean _Pause = false;
		public Boolean IsPause
		{
			get { return _Pause; }
			set { 
				_Pause = value;
				SetPause(_Pause);
			}
		}

		/// 開放
		void OnDestroy()
		{
			Stop();
		}

		private Model _ConnectedModel = Model.Unknown;
		public Model ConnectedModel
		{
			get { return _ConnectedModel; }
		}

		///最後のパターンとレベル
		private Pattern Old_pattern = Pattern.ClockWise;
		private Int32 Old_level = 0;
		private Boolean Old_Pause = false;
		//A10操作用のポート.
		SerialPort port = null;

		// MEMO
		// A10Cycloneの操作に使えるコマンドは下記の通り
		// COMポートとのシリアル通信で専用レシーバの操作
		// Baudrate:19200bps ,Parity:None,DataBits:8,StopBits:one;
		// port.DtrEnable = true, port.RtsEnable = true
		// 送信データ：3byte
		// F0 01 00 : 機種判定
		// 　戻り値無し(A10SA付属黒色アダプタ) → A10SA
		//　「01」（U.F.O.SA付属灰色アダプタ) → A10SA
		//　「02」（同） → U.F.O.SA
		//　「FF」（同） → 接続なし
		// 01 01 XX ：回転実行
		// XX: 00(停止)/01～7F(正回転)/80～FF(逆回転)
		// TODO 後でデバイスのCOM番号取得方法見直し
		// 更新周期は200ms程度にしないと、デバイスが受け取れない可能性あり

		public bool OpenDevice(string comPortName = "COM4")
		{
			// すでに接続済であれば、処理しない
			if (IsDeviceEnable == false)
			{
				// ポート名が指定されている
				if (comPortName != null)
				{
					try
					{
						// A10Cycloneのオープン
						port = new SerialPort(comPortName, 19200, Parity.None, 8, StopBits.One);
						port.Open();

						// 通信設定
						port.DtrEnable = true;
						port.RtsEnable = true;
						port.ReadTimeout = 100; // タイムアウト時間は100msとする

						// 機種判定実施
						port.Write(ModelCheckCmd, 0, ModelCheckCmd.Length);

						// 結果を受け取る
						Int32 result = port.ReadByte();

						// 結果が接続機器として認識可能か確認.
						switch (result)
						{
							// A10サイクロンSA
							case (Int32)Model.A10Cyclone:
								_DeviceEnable = true;
								_ConnectedModel = Model.A10Cyclone;
								break;

							// U.F.O SA
							case (Int32)Model.UFOSA:
								_DeviceEnable = true;
								_ConnectedModel = Model.UFOSA;
								break;

							// 未接続 or 未定義.
							case (Int32)Model.Unknown:
							default:
								_DeviceEnable = false;
								_ConnectedModel = Model.Unknown;
								port.Close();
								port.Dispose();
								port = null;
								break;
						}
					}
					catch (System.TimeoutException)
					{
						// A10 Cyclone SAと判断する.
						// 旧い型のA10サイクロンだと、機種判別が効かないらしいため、タイムアウト時もA10サイクロンとする
						_DeviceEnable = true;
						_ConnectedModel = Model.A10Cyclone;
					}
					catch (System.Exception e)
					{
						Console.WriteLine(e);
						_DeviceEnable = false;
						_ConnectedModel = Model.Unknown;
						if (port != null)
						{
							if (port.IsOpen)
							{
								port.Close();
							}
							port.Dispose();
							port = null;
						}
					}
				}
				else
				{
					_DeviceEnable = false;
					_ConnectedModel = Model.Unknown;
				}
			}

			return _DeviceEnable;
		}

		public void CloseDevice()
		{
			//デバイスが取得できていない場合は無視をする
			if (!IsDeviceEnable) { return; }

			_DeviceEnable = false;
			_ConnectedModel = Model.Unknown;
			port.Close();
			port.Dispose();
			port = null;
		}

		// デバイス値更新
		public void StatusUpDate()
		{
			//デバイスが取得できていない場合は無視をする
			if (!IsDeviceEnable) { return; }

			Byte[] buffer = new Byte[] { (Byte)_ConnectedModel, 0x01, 0x00 };

			//パターン、Level、ポーズが変わった場合変更をする
			if (Old_pattern != pattern || Old_level != level || Old_Pause != _Pause)
			{
				// 送信データを設定
				if (pattern == Pattern.ClockWise)
				{
					buffer[2] = (Byte)level;
				}
				else
				{
					buffer[2] = (Byte)(level + Offset_CounterClockWise);
				}
				
				// ポーズ要求時は0x00を送信.
				if (_Pause == true)
				{
					buffer[2] = 0x00;
				}

				// 最終のパターンとレベルを設定.
				Old_pattern = pattern;
				Old_level = level;
				Old_Pause = _Pause;

				// デバイスに送信
				port.Write(buffer, 0, buffer.Length);
			}
		}

		/// パターンとレベルを更新する
		public void SetPatternAndLevel(Pattern SetPattern, int SetLevel)
		{
			// ポーズ強制解除
			_Pause = false;
			_pattern = SetPattern;
			_level = SetLevel;

			StatusUpDate();
		}

		//ポーズ&ポーズ切り替え
		public void Pause()
		{
			//デバイスが取得できていない場合は無視をする
			if (!IsDeviceEnable) { return; }
			//ポーズ状態を逆転させる
			SetPause(!IsPause);
		}
		//ポーズ状態を設定する
		private void SetPause(bool Flag)
		{
			_Pause = Flag;

			StatusUpDate();
		}

		//停止をする
		public void Stop()
		{
			SetPatternAndLevel(Pattern.ClockWise, Level_Stop);

			StatusUpDate();
		}

		/// 値の最大最小を制限する
		private static int Clamp(int value, int Min, int Max)
		{
			if (value < Min)
			{
				return Min;
			}
			else if (Max < value)
			{
				return Max;
			}
			else
			{
				return value;
			}
		}
	}
}
