using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;
using UnityInjector.Attributes;

using A10Cyclone;
using System.Xml.Serialization;

namespace CM3D2.A10Cyclone.plugin
{
    [PluginFilter("CM3D2x64"), PluginFilter("CM3D2x86"), PluginFilter("CM3D2VRx64")]
    [PluginName(PluginName), PluginVersion(Version)]
    public class A10Cyclone : UnityInjector.PluginBase
    {
        private const string PluginName = "CM3D2 A10Cyclone Plugin";
        private const string Version = "0.0.0.1";


        //XMLファイルの読み込み先
        private readonly string XmlFileDirectory = Application.dataPath + "/../UnityInjector/Config/A10CycloneXml/";

        //各種設定項目
        private readonly float TimePerInit = 1.00f;
        private readonly float WaitFirstInit = 5.00f;

        //初期化完了かどうか
        private bool InitCompleted = false;

        //動作中のステータス
        private string yotogi_group_name = "";          //夜伽グループ名
        private string yotogi_name = "";                //夜伽名
        private int iLastExcite = 0;                    //興奮値
        private Yotogi.ExcitementStatus yExciteStatus;  //興奮ステータス
        private YotogiPlay.PlayerState bInsertFuck = YotogiPlay.PlayerState.Normal;               //挿入状態かどうか
        private string Personal="";

        // CM3D2関連の参照
        private int sceneLevel;//シーンレベル
        private Maid maid;
        private YotogiManager yotogiManager;
        private YotogiPlayManager yotogiPlayManager;
        private Action<Yotogi.SkillData.Command.Data> orgOnClickCommand;

        //A10Cyclone関連
        private A10CycloneClass a10Cyclone = new A10CycloneClass();
        private static bool CycloneGUI = false;
        private Rect windowRect = new Rect(20, 20, 120, 50);
        private int NowPattern = 0;
        private int NowLevel = 0;

        //サイクロン用の設定ファイル郡
        private A10CycloneConfig.YotogiItem YotogiItem = null;
        private SortedDictionary<string, A10CycloneConfig> A10CycloneConfigDictionay = new SortedDictionary<string, A10CycloneConfig>();
        private Dictionary<string, A10CycloneConfig.LevelItem> A10CycloneLevelsDict = new Dictionary<string, A10CycloneConfig.LevelItem>();

        //コルーチン
        private IEnumerator CycloneEnum = null;

        #region MonoBehaviour methods
        public void Start()
        {
            //設定用のディレクトリを生成する
            if (!System.IO.Directory.Exists(XmlFileDirectory))
            {
                System.IO.Directory.CreateDirectory(XmlFileDirectory);
                Debug.Log("ディレクトリ生成:" + XmlFileDirectory);
            }

            //デッバク用ログ初期設定
            DebugManager.DebugMode = false;
        }

        public void Update()
        {
            if (sceneLevel == 14)
            {
                //ログを表示する
                if (Input.GetKeyDown(KeyCode.F10))
                {
                    DebugManager.DebugMode = !DebugManager.DebugMode;
                }
                //A10サイクロン関連のダイアログ表示
                if (Input.GetKeyDown(KeyCode.F11))
                {
                    CycloneGUI = !CycloneGUI;
                }
            }
        }

        public void OnGUI()
        {
            //デバッグ機能
            if (InitCompleted && sceneLevel == 14)
            {
                //ログ表示
                DebugManager.GUIText();
                //A10サイクロン関連のデバッグ用ウィンドウ
                if (CycloneGUI)
                    windowRect = GUILayout.Window(0, windowRect, GUIWindow, "A10Cyclone");
            }
        }

        public void OnApplicationQuit()
        {
			A10CycloneInit();
        }

        public void A10CycloneInit()
        {
            //Stopする
            a10Cyclone.SetPatternAndLevel(0, 0);

            //変数群初期化
            yotogi_group_name = "";
            yotogi_name = "";
            iLastExcite = 0;
            yExciteStatus = 0;
            bInsertFuck = YotogiPlay.PlayerState.Normal;
            Personal = "";

            NowPattern = 0;
            NowLevel = 0;
        }

        //シーンがロードされた場合
        public void OnLevelWasLoaded(int level)
        {
            //夜伽シーンの場合初期化をする
            if (level == 14)
            {
                //起動時に読み込み
                LoadCycloneXMLFile();
                //初期化
                StartCoroutine(initCoroutine(TimePerInit));
            }
            A10CycloneInit();

            //読み込んだシーンレベルを保存
            sceneLevel = level;
        }
        #endregion

        #region MonoBehaviour Coroutine

        private IEnumerator initCoroutine(float waitTime)
        {
            yield return new WaitForSeconds(WaitFirstInit);
            while (!(InitCompleted = Yotogi_initialize())) yield return new WaitForSeconds(waitTime);
            DebugManager.Log("Initialization complete [ Load SeenLevel:" + sceneLevel.ToString() + "]");
        }

        private IEnumerator CycloneCoroutine(int iLastExcite, A10CycloneConfig.YotogiItem YotogiItem, Dictionary<string, A10CycloneConfig.LevelItem> A10CyclonePattanDict, bool InsertFlg, string Personal)
        {
            //興奮状態のステータス
            yExciteStatus = YotogiPlay.GetExcitementStatus(iLastExcite);

			// 指定コマンドの制御状態をループする.
			while (true)
			{
				foreach (A10CycloneConfig.Control Item in YotogiItem.ControlData)
				{
					//性格の指定があるかどうか(未指定の場合はそのまま実行)
					if (Item.Personal == "" || Item.Personal == Personal)
					{
						//挿入時に挿入フラグがあった場合もしくはそれ以外
						if ((Item.Insert && InsertFlg) || Item.Insert == false)
						{
							//現在のPatternとLevel
							A10CycloneClass.Pattern SetPattan = a10Cyclone.pattern;
							int SetLevel = a10Cyclone.level;

							//Patternの定義があれば更新
							if (0 == Item.Pattern)
							{
								SetPattan = A10CycloneClass.Pattern.ClockWise;

							}
							else if (1 == Item.Pattern)
							{
								SetPattan = A10CycloneClass.Pattern.CounterClockWise;
							}

							//Levelの定義があれば更新
							if (-1 < Item.Level)
							{
								SetLevel = Clamp(Item.Level, A10CycloneClass.Level_Min, A10CycloneClass.Level_Max);
							}
							//LevelNameの定義がある場合
							if (Item.LvName != "")
							{
								if (A10CyclonePattanDict.ContainsKey(Item.LvName))
								{
									//興奮値を元にLevelを更新
									SetLevel = Clamp(GetLevel(yExciteStatus, A10CyclonePattanDict[Item.LvName]), A10CycloneClass.Level_Min, A10CycloneClass.Level_Max);
								}
								else
								{
									DebugManager.Log("LevelNameの定義が見つかりません");
								}
							}

							//ディレイ
							if (0.0f < Item.Delay)
							{
								yield return new WaitForSeconds(Item.Delay);
							}

							//振動を開始する
							if (SetLevel != a10Cyclone.level || SetPattan != a10Cyclone.pattern)
							{
								//Cycloneの振動処理
								a10Cyclone.SetPatternAndLevel(SetPattan, SetLevel);
								//GUI用に更新をする。
								NowPattern = (Int32)a10Cyclone.pattern;
								NowLevel = a10Cyclone.level;
							}

							//ログを追加
							DebugManager.Log("cycloneX10 : [Pattern:" + a10Cyclone.pattern + "][Level:" + a10Cyclone.level + "][Delay:" + Item.Delay + "][Time:" + Item.Time + "]");

							//継続タイム
							if (0.0f < Item.Time)
							{
								yield return new WaitForSeconds(Item.Time);
							}
							else
							{
								// 継続時間の指定が無い場合、0.1秒毎に次の処理へ移行する.
								yield return new WaitForSeconds(0.1f);
							}
						}
					}
				}
			}
		}
        #endregion

        #region MonoBehaviour GUI関連

        /// <summary>
        /// Cyclone用の操作Window
        /// </summary>
        /// <param name="windowID"></param>
        private void GUIWindow(int windowID)
        {
            GUILayout.BeginHorizontal();
            {
                if (a10Cyclone.IsDeviceEnable)
                {
                    GUILayout.Label("接続状態: 接続中");
                }
                else
                {
                    GUILayout.Label("接続状態: 未接続");
                }
                if (GUILayout.Button("XML再読み込み"))
                {
                    LoadCycloneXMLFile();
                }

                //通常このXML出力機能は使わない
                //if (GUILayout.Button("出力"))
                //{
                //    CreateAllYotogiXML();
                //}
                
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Pattern");

            GUILayout.BeginHorizontal();
            {
                for (int i = 0; i < 2; i++)
                {
                    if (GUILayout.Toggle(i == NowPattern, i.ToString()))
                    {
                        NowPattern = i;
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Level");

            GUILayout.BeginHorizontal();
            {
                for (int i = 0; i < 10; i++)
                {
                    if (GUILayout.Toggle((i*10) == NowLevel, (i*10).ToString()))
                    {
                        NowLevel = (i*10);
                    }
                }
                if (NowLevel != a10Cyclone.level || NowPattern != (Int32)a10Cyclone.pattern)
                {
					a10Cyclone.SetPatternAndLevel(NowPattern == 0 ? A10CycloneClass.Pattern.ClockWise : A10CycloneClass.Pattern.CounterClockWise, NowLevel);
                    NowPattern = (Int32)a10Cyclone.pattern;
                    NowLevel = a10Cyclone.level;
                    DebugManager.Log("SetPatternAndLevel:" + a10Cyclone.pattern + "," + a10Cyclone.level);
                }
            }
            GUILayout.EndHorizontal();

			GUILayout.Label("デバイス接続");
			GUILayout.BeginHorizontal();

			var portNames = System.IO.Ports.SerialPort.GetPortNames();

			for (var i = 0; i < portNames.Length; i++)
			{
				if (GUILayout.Button(portNames[i]))
				{
					if (a10Cyclone.IsDeviceEnable)
					{
						a10Cyclone.SetPatternAndLevel(A10CycloneClass.Pattern.ClockWise, 0);
						a10Cyclone.CloseDevice();
					}

					a10Cyclone.OpenDevice(portNames[i]);
				}
			}
			GUILayout.EndHorizontal();

			GUILayout.Label("デバイス切断");

			GUILayout.BeginHorizontal();

			if (GUILayout.Button("切断"))
			{
				if (a10Cyclone.IsDeviceEnable)
				{
					a10Cyclone.SetPatternAndLevel(A10CycloneClass.Pattern.ClockWise, 0);
					a10Cyclone.CloseDevice();
				}
			}

			GUILayout.EndHorizontal();


			if (GUILayout.Button("ポーズ:" + a10Cyclone.IsPause))
            {
				a10Cyclone.Pause();
                DebugManager.Log("ポーズ:" + a10Cyclone.IsPause.ToString());
            }

            GUILayout.Label("夜伽グループ:" + yotogi_group_name);
            GUILayout.Label("夜伽コマンド:" + yotogi_name);
            GUILayout.Label("興奮値　　　:" + iLastExcite.ToString() + "[" + yExciteStatus+"]");
            GUILayout.Label("挿入状態　　:" + bInsertFuck.ToString());
            GUILayout.Label("メイド性格　:" + Personal);
            GUI.DragWindow();
        }

        /// <summary>
        /// 画面上に常に表示をするデバッグ機能
        /// </summary>
        private static class DebugManager
        {
            public static bool DebugMode
            {
                get { return _DebugMode; }
                set { _DebugMode = value; }
            }
            //デバッグの最大行数
            private const int MaxDebugText = 10;

            private static bool _DebugMode = false;
            private static Queue<string> DebugTextList = new Queue<string>();
            private static Rect TextAreaRect = new Rect(10, 10, Screen.width / 2, Screen.height - 20);

            //デバッグ情報として出力する内容
            public static void Log(string DebugText)
            {
                if (MaxDebugText < DebugTextList.Count)
                {
                    //先頭の物を削除
                    DebugTextList.Dequeue();
                    DebugTextList.Enqueue(DebugText);
                }
                else
                {
                    DebugTextList.Enqueue(DebugText);
                }
            }
            //クリア
            public static void Clear()
            {
                DebugTextList.Clear();
            }

            //OnGUI上で実行すること
            public static void GUIText()
            {
                if (DebugMode)
                {
                    GUILayout.BeginArea(TextAreaRect);
                    foreach (string log in DebugTextList)
                    {
                        GUILayout.Label(log);
                    }
                    GUILayout.EndArea();
                }
            }

        }
        #endregion

        #region UnityInjector関連
        private bool Yotogi_initialize()
        {
			// 初期化
			a10Cyclone.OpenDevice();

            //メイドを取得
            this.maid = GameMain.Instance.CharacterMgr.GetMaid(0);
            if (!this.maid) return false;

            // 夜伽コマンドフック
            {
                this.yotogiManager = getInstance<YotogiManager>();
                if (!this.yotogiManager) return false;
                this.yotogiPlayManager = getInstance<YotogiPlayManager>();
                if (!this.yotogiPlayManager) return false;

                YotogiCommandFactory cf = getFieldValue<YotogiPlayManager, YotogiCommandFactory>(this.yotogiPlayManager, "command_factory_");
                if (IsNull(cf)) return false;

                try
                {
                    //YotogiPlayManagerのコールバック
                    cf.SetCommandCallback(new YotogiCommandFactory.CommandCallback(this.OnYotogiPlayManagerOnClickCommand));
                }
                catch (Exception ex)
                {
                    DebugManager.Log(string.Format("Error - SetCommandCallback() : {0}", ex.Message));
                    return false;
                }

                this.orgOnClickCommand = getMethodDelegate<YotogiPlayManager, Action<Yotogi.SkillData.Command.Data>>(this.yotogiPlayManager, "OnClickCommand");
                if (IsNull(this.orgOnClickCommand)) return false;
            }
            return true;
        }

        public void OnYotogiPlayManagerOnClickCommand(Yotogi.SkillData.Command.Data command_data)
        {
            YotogiPlay.PlayerState OldPlayerState = bInsertFuck;

            //実際の動作をする
            orgOnClickCommand(command_data);

            //メイドの性格を取得
            Personal = this.maid.Param.status.personal.ToString();

            //夜伽グループ名
            yotogi_group_name = command_data.basic.group_name;
            //夜伽コマンド名
            yotogi_name = command_data.basic.name;
            //興奮値
            iLastExcite = maid.Param.status.cur_excite;
            //興奮状態のステータス
            yExciteStatus = YotogiPlay.GetExcitementStatus(iLastExcite);
            //挿入状態かどうか
            bInsertFuck = getFieldValue<YotogiPlayManager, YotogiPlay.PlayerState>(this.yotogiPlayManager, "player_state_");

            //PlayerStateがNormalからInsertになる場合
            bool InsertFlg = (OldPlayerState == YotogiPlay.PlayerState.Normal && bInsertFuck == YotogiPlay.PlayerState.Insert);

			//Cycloneを実行する
			A10CycloneEvents(yotogi_group_name, yotogi_name, iLastExcite, InsertFlg, Personal);
        }
        #endregion

        #region A10Cyclone関連
        private void A10CycloneEvents(string yotogi_group_name, string yotogi_name, int iLastExcite, bool InsertFlg ,string Personal)
        {
            //前回のコルーチンが走っている場合は停止をする
            if (CycloneEnum != null) { StopCoroutine(CycloneEnum); }

            YotogiItem = null;
            A10CycloneLevelsDict.Clear();
            if (A10CycloneConfigDictionay.ContainsKey(yotogi_group_name))
            {
                //振動パターンのDictionayを生成
                foreach (A10CycloneConfig.LevelItem Item in A10CycloneConfigDictionay[yotogi_group_name].LevelList)
                {
                    if (!A10CycloneLevelsDict.ContainsKey(Item.LvName))
                    {
						A10CycloneLevelsDict.Add(Item.LvName, Item);
                    }
                    else
                    {
                        Debug.Log("Warning : LevelNameが重複しています。[" + Item.LvName + "]");
                    }
                }
                //設定ファイルを確定する
                foreach (A10CycloneConfig.YotogiItem Item in A10CycloneConfigDictionay[yotogi_group_name].YotogiCXConfig.YotogiList)
                {
                    if (Item.Yotogi_Name == yotogi_name)
                    {
                        YotogiItem = Item;
                        break;
                    }
                }
                if (YotogiItem != null)
                {
                    DebugManager.Log("実行:" + YotogiItem.Yotogi_Name);

                    //コルーチンを開始する
                    CycloneEnum = CycloneCoroutine(iLastExcite, YotogiItem, A10CycloneLevelsDict, InsertFlg, Personal);
                    StartCoroutine(CycloneEnum);
                }
            }
        }

        //導入されている全夜伽コマンドデータ用の設定ファイルを一括作成
        void CreateAllYotogiXML()
        {
            for (int cat = 0; cat < (int)Yotogi.Category.MAX; cat++)
            {
                SortedDictionary<int, Yotogi.SkillData> data = Yotogi.skill_data_list[cat];
                foreach (Yotogi.SkillData sd in data.Values)
                {
					A10CycloneConfig XML = new A10CycloneConfig();
                    XML.EditInformation.EditName = "UserName";
                    XML.EditInformation.TimeStamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    XML.EditInformation.Comment = "";

                    XML.YotogiCXConfig.GroupName = sd.name;
                    XML.LevelList.Clear();
                    XML.LevelList.Add(PatternItem("STOP", 0, 0, 0, 0));
                    XML.LevelList.Add(PatternItem("PreSet1", 10, 30, 50, 70));
                    XML.LevelList.Add(PatternItem("PreSet2", 20, 40, 60, 80));
                    XML.LevelList.Add(PatternItem("PreSet3", 60, 80, 100, 120));

                    foreach (var comData in sd.command.data)
                    {
						A10CycloneConfig.YotogiItem YotogiListData = new A10CycloneConfig.YotogiItem();

                        YotogiListData.Yotogi_Name = comData.basic.name;
                        YotogiListData.ControlData.Add(ControlItem(0f, "STOP"));

                        XML.YotogiCXConfig.YotogiList.Add(YotogiListData);
                    }

                    XMLWriter<A10CycloneConfig>(XmlFileDirectory + sd.name + ".xml", XML);
                }
            }
        }

        private A10CycloneConfig.LevelItem PatternItem(string Name, int LV0, int LV1, int LV2, int LV3)
        {
			A10CycloneConfig.LevelItem PItem = new A10CycloneConfig.LevelItem();
            PItem.LvName = Name;
            PItem.Lv0 = LV0;
            PItem.Lv1 = LV1;
            PItem.Lv2 = LV2;
            PItem.Lv3 = LV3;
            return PItem;
        }
        private A10CycloneConfig.Control ControlItem(float diray , string Name)
        {
			A10CycloneConfig.Control Cont = new A10CycloneConfig.Control();
            Cont.Delay = diray;
            Cont.LvName = Name;
            return Cont;
        }

        /// <summary>
        /// 振動設定用のXMLファイル
        /// </summary>
        private void LoadCycloneXMLFile()
        {
            Debug.Log("読み込み開始");
            A10CycloneConfigDictionay.Clear();
            string[] files = System.IO.Directory.GetFiles(XmlFileDirectory, "*.xml", System.IO.SearchOption.AllDirectories);
            foreach (string file in files)
            {
                try
                {
                    if (System.IO.File.Exists(file))
                    {
						A10CycloneConfig XML = XMLLoader<A10CycloneConfig>(file);
                        if (!A10CycloneConfigDictionay.ContainsKey(XML.YotogiCXConfig.GroupName))
                        {
							A10CycloneConfigDictionay.Add(XML.YotogiCXConfig.GroupName, XML);
                        }
                    }
                }
                catch (Exception err)
                {
                    //エラーが有った場合のみエラー内容を表示
                    Debug.Log(System.IO.Path.GetFileName(file) + ":LoadError [" + err + "] ");
                }
            }
            Debug.Log("A10Cycloneの設定ファイル " + A10CycloneConfigDictionay.Count + "個 読み込み完了");
        }

        #endregion

        #region 各種関数群
        /// <summary>
        /// XMLデータの読み込み
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <returns></returns>
        internal static T XMLLoader<T>(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            StreamReader reader = new System.IO.StreamReader(stream, new System.Text.UTF8Encoding(false));
            T load = (T)serializer.Deserialize(reader);
            reader.Close();

            return load;
        }
        /// <summary>
        /// XMLデータの書き込み
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="save"></param>
        public static void XMLWriter<T>(string path, T save)
        {
            //XMLファイルに保存する
            XmlSerializer serializer = new XmlSerializer(typeof(T));
            System.IO.StreamWriter writer = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false));
            serializer.Serialize(writer, save);
            writer.Close();
        }


        //ゲームオブジェクトの検索と取得
        internal static T getInstance<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindObjectOfType(typeof(T)) as T;
        }
        //IsNUll
        internal static bool IsNull<T>(T t) where T : class
        {
            return (t == null) ? true : false;
        }

        internal static TResult getFieldValue<T, TResult>(T inst, string name)
        {
            if (inst == null) return default(TResult);

            FieldInfo field = getFieldInfo<T>(name);
            if (field == null) return default(TResult);

            return (TResult)field.GetValue(inst);
        }
        internal static FieldInfo getFieldInfo<T>(string name)
        {
            BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            return typeof(T).GetField(name, bf);
        }

        internal static TResult getMethodDelegate<T, TResult>(T inst, string name)
            where T : class
            where TResult : class
        {
            return Delegate.CreateDelegate(typeof(TResult), inst, name) as TResult;
        }

        private int GetLevel(Yotogi.ExcitementStatus Status, A10CycloneConfig.LevelItem LevelItem)
        {
            try
            {
                switch (Status)
                {
                    case Yotogi.ExcitementStatus.Minus:
                        {
                            return LevelItem.Lv0;
                        }
                    case Yotogi.ExcitementStatus.Small:
                        {
                            return LevelItem.Lv1;
                        }
                    case Yotogi.ExcitementStatus.Medium:
                        {
                            return LevelItem.Lv2;
                        }
                    case Yotogi.ExcitementStatus.Large:
                        {
                            return LevelItem.Lv3;
                        }
                    default:
                        {
                            return -1;
                        }
                }
            }
            catch
            {
                Debug.Log("Error:GetLevel");
                return -1;
            }
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

        #endregion

    }
}