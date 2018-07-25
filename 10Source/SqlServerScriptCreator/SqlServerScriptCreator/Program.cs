using log4net;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;

namespace SqlServerScriptCreator
{
    public class Program
    {
        /// <summary>
        /// 出力したスクリプト数
        /// </summary>
        private static int _outputScriptCount;

        /// <summary>
        /// 実行EXEのパス
        /// </summary>
        private static string _pathAssembly;

        public static void Main(string[] args)
        {
            ILog logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

            try
            {
                if (2 != args.Count())
                {
                    var errMessage = "[パラメーター数誤り]パラメーター数は、2つのみ指定して下さい。";
                    logger.Error(errMessage);
                    System.Console.WriteLine(errMessage);
                    return;
                }

                var objectType = string.Empty;　 // オブジェクトのタイプ
                var csvFilePath = string.Empty;  // CSVファイルのパス

                #region パラメータ値を取得
                var cn = 0;
                foreach (var arg in args)
                {
                    cn++;

                    if (1 == cn)
                    { // オブジェクトのタイプ
                        objectType = arg;
                    }
                    else if (2 == cn)
                    { // CSVファイルのパス
                        csvFilePath = arg;
                    }
                }
                #endregion

                // CSVファイルの内容保持用
                var csvDataList = new List<CsvData>();

                #region パラメータ判定

                // パラメータ判定
                var ret = JudgeParameter(logger, objectType, csvFilePath, csvDataList);
                if (!ret)
                {
                    return;
                }

                #endregion

                _pathAssembly = System.Configuration.ConfigurationManager.AppSettings.Get("OutputScriptPath");

                var srv = new Server();
                srv.ConnectionContext.ConnectionString = ConfigurationManager.ConnectionStrings["sqlsvr"].ConnectionString;

                var targetDataBase = ConfigurationManager.AppSettings.Get("TargetDataBase");

                var scriptDorpOrCreate = ConfigurationManager.AppSettings.Get("ScriptDorpOrCreate");
                if (string.IsNullOrEmpty(scriptDorpOrCreate))
                {
                    scriptDorpOrCreate = "0";
                }

                // 作成するスクリプトの種別リスト(true：DROP、false：CREATE)
                var createScriptTypeList = new List<bool>();
                if ("0" == scriptDorpOrCreate)
                {
                    createScriptTypeList.Add(false);
                }
                if ("1" == scriptDorpOrCreate)
                {
                    createScriptTypeList.Add(true);
                }
                if ("2" == scriptDorpOrCreate)
                {
                    createScriptTypeList.Add(true);
                    createScriptTypeList.Add(false);
                }

                foreach (Database db in srv.Databases)
                {
                    if (db.Name != targetDataBase)
                    {
                        // 対象データベースでない場合は、対象外
                        continue;
                    }

                    if (db.IsSystemObject)
                    {
                        // システムオブジェクトの場合は、対象外
                        continue;
                    }

                    var appendFileFlg = 0;

                    // 作成するスクリプトの種別毎
                    foreach (var createScriptType in createScriptTypeList)
                    {
                        // 追記指示
                        var appendFile = Convert.ToBoolean(appendFileFlg);

                        // スクリプト作成オプション
                        var scrp = new Scripter(srv);

                        #region スクリプト作成オプションの設定
                        // オブジェクトの存在を確認するか
                        scrp.Options.IncludeIfNotExists = true;

                        // Transact を生成するか
                        //scrp.Options.ScriptDrops = true;

                        // すべての依存オブジェクトを生成されるスクリプトに含めるか
                        scrp.Options.WithDependencies = false;

                        // インデックスが生成されるスクリプトに含めるか
                        scrp.Options.Indexes = true;

                        // クラスター化インデックスを定義するステートメントが生成されるスクリプトに含めるか
                        scrp.Options.ClusteredIndexes = true;

                        // Transact が生成されるスクリプトに含まれるか
                        scrp.Options.AnsiPadding = true;

                        // すべての宣言参照整合性制約が生成されるスクリプトに含めるか
                        scrp.Options.DriAllConstraints = true;

                        // スクリプトの照合順序
                        scrp.Options.NoCollation = true;

                        // 拡張プロパティが生成されるスクリプトに含めるか
                        scrp.Options.ExtendedProperties = false;

                        // オブジェクトを削除する削除句を、生成されるスクリプトに含めるか
                        scrp.Options.ScriptDrops = createScriptType;
                        #endregion

                        #region テーブルスクリプト作成
                        if ("U" == objectType)
                        {
                            CreateScriptTable(scrp, db, csvDataList, appendFile);
                        }
                        #endregion

                        #region ビューのスクリプト作成
                        if ("V" == objectType)
                        {
                            CreateScriptView(scrp, db, csvDataList, appendFile);
                        }
                        #endregion

                        #region ストアド プロシージャのスクリプト作成
                        if ("P" == objectType)
                        {
                            CreateScriptStoredProcedure(scrp, db, csvDataList, appendFile);
                        }
                        #endregion

                        #region ユーザー定義関数のスクリプト作成
                        if ("F" == objectType)
                        {
                            CreateScriptUserDefinedFunction(scrp, db, csvDataList, appendFile);
                        }
                        #endregion

                        #region テーブルトリガーのスクリプト作成
                        if ("TT" == objectType)
                        {
                            CreateScriptTableTrigger(scrp, db, csvDataList, appendFile);
                        }
                        #endregion

                        #region トリガーのスクリプト作成
                        if ("T" == objectType)
                        {
                            CreateScriptTrigger(scrp, db, csvDataList, appendFile);
                        }
                        #endregion

                        // 追記指示指定
                        appendFileFlg++;
                    }
                }

                var outMsg = string.Format("スクリプトファイルを{0}件、作成しました。対象オブジェクトタイプ：{1}", _outputScriptCount, objectType);
                logger.Info(outMsg);
                System.Console.WriteLine(outMsg);
            }
            catch (Exception ex)
            {
                var errMessage = string.Format("スクリプトの生成に失敗しました。ErrMassage={0}", ex.ToString());
                logger.Error(errMessage);
                System.Console.WriteLine(errMessage);
            }
        }

        #region パラメータ判定
        /// <summary>
        /// パラメータ判定
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="objectType">オブジェクトタイプ</param>
        /// <param name="csvFilePath">CSVファイルのパス</param>
        /// <param name="csvDataList">CSVデータリスト</param>
        /// <returns>実行結果(true:正常)</returns>
        private static bool JudgeParameter(ILog logger, string objectType, string csvFilePath, List<CsvData> csvDataList)
        {
            #region 第1：オブジェクトタイプ
            {
                if ("U" == objectType)
                {
                    // U = テーブル(ユーザー定義)
                }
                else if ("V" == objectType)
                {
                    // V = ビュー の場合
                }
                else if ("P" == objectType)
                {
                    // P = ストアド プロシージャ の場合
                }
                else if ("F" == objectType)
                {
                    // F = ユーザー定義関数 の場合
                }
                else if ("TT" == objectType)
                {
                    // TT = テーブルトリガー の場合
                }
                else if ("T" == objectType)
                {
                    // T = トリガー の場合
                }
                else
                {
                    var errMessage = string.Format("想定外のオブジェクトタイプです。オブジェクトタイプ[{0}]", objectType);
                    logger.Error(errMessage);
                    System.Console.WriteLine(errMessage);
                    return false;
                }
            }
            #endregion

            #region 第2：ファイルパス
            {
                if (string.IsNullOrEmpty(csvFilePath))
                {
                    var errMessage = "CSVのファイルパスが指定されていません。";
                    logger.Error(errMessage);
                    System.Console.WriteLine(errMessage);
                    return false;
                }

                if (!File.Exists(csvFilePath))
                {
                    var errMessage = "CSVのファイルが存在しません。";
                    logger.Error(errMessage);
                    System.Console.WriteLine(errMessage);
                    return false;
                }

                // CSVファイル読み込み
                using (var sw = new StreamReader(csvFilePath, false))
                {
                    var line = string.Empty;
                    while ((line = sw.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }

                        var lineDatas = line.Split(',');

                        if (2 != lineDatas.Count())
                        {
                            continue;
                        }

                        var csvData = new CsvData();
                        csvData.OwnerName = lineDatas[0];
                        csvData.ObjectName = lineDatas[1];
                        csvDataList.Add(csvData);
                    }
                }
            }
            #endregion

            return true;
        }
        #endregion

        #region テーブルのスクリプト作成
        /// <summary>
        /// テーブルのスクリプト作成
        /// </summary>
        /// <param name="scrp">出力するスクリプトの設定</param>
        /// <param name="db">データベース</param>
        /// <param name="csvDataList">CSV情報</param>
        /// <param name="appendFile">追記指示</param>
        private static void CreateScriptTable(Scripter scrp, Database db, List<CsvData> csvDataList, bool appendFile)
        {
            // U = テーブル(ユーザー定義) の場合
            var objectTypeName = "Table";

            foreach (Table tb in db.Tables)
            {
                if (tb.IsSystemObject)
                {
                    // システムオブジェクトの場合は、対象外
                    continue;
                }

                // 対象オブジェクト情報を設定
                var targetObjectInfo = new CsvData();
                targetObjectInfo.OwnerName = tb.Owner;
                targetObjectInfo.ObjectName = tb.Name;

                if (!csvDataList.Any(x => x.ObjectName == targetObjectInfo.ObjectName
                                       && x.OwnerName == targetObjectInfo.OwnerName))
                {
                    // CSVに該当データなし
                    continue;
                }

                // スクリプト情報を取得
                var sc = scrp.Script(new Urn[] { tb.Urn });

                Console.WriteLine(tb.Name);

                // スクリプト作成
                CreateScript(db.Name, objectTypeName, targetObjectInfo, sc, appendFile);
            }
        }
        #endregion

        #region ビューのスクリプト作成
        /// <summary>
        /// ビューのスクリプト作成
        /// </summary>
        /// <param name="scrp">出力するスクリプトの設定</param>
        /// <param name="db">データベース</param>
        /// <param name="csvDataList">CSV情報</param>
        /// <param name="appendFile">追記指示</param>
        private static void CreateScriptView(Scripter scrp, Database db, List<CsvData> csvDataList, bool appendFile)
        {
            // V = ビュー の場合
            var objectTypeName = "View";

            foreach (View vw in db.Views)
            {
                if (vw.IsSystemObject)
                {
                    // システムオブジェクトの場合は、対象外
                    continue;
                }

                // 対象オブジェクト情報を設定
                var targetObjectInfo = new CsvData();
                targetObjectInfo.OwnerName = vw.Owner;
                targetObjectInfo.ObjectName = vw.Name;

                if (!csvDataList.Any(x => x.ObjectName == targetObjectInfo.ObjectName
                                       && x.OwnerName == targetObjectInfo.OwnerName))
                {
                    // CSVに該当データなし
                    continue;
                }

                // スクリプト情報を取得
                var sc = scrp.Script(new Urn[] { vw.Urn });

                // スクリプト作成
                CreateScript(db.Name, objectTypeName, targetObjectInfo, sc, appendFile);
            }
        }
        #endregion

        #region ストアド プロシージャのスクリプト作成
        /// <summary>
        /// ストアド プロシージャのスクリプト作成
        /// </summary>
        /// <param name="scrp">出力するスクリプトの設定</param>
        /// <param name="db">データベース</param>
        /// <param name="csvDataList">CSV情報</param>
        /// <param name="appendFile">作成するスクリプトの種別</param>
        private static void CreateScriptStoredProcedure(Scripter scrp, Database db, List<CsvData> csvDataList, bool appendFile)
        {
            // P = SQL ストアド プロシージャ  の場合
            var objectTypeName = "StoredProcedure";

            foreach (StoredProcedure sp in db.StoredProcedures)
            {
                if (sp.IsSystemObject)
                {
                    // システムオブジェクトの場合は、対象外
                    continue;
                }

                // 対象オブジェクト情報を設定
                var targetObjectInfo = new CsvData();
                targetObjectInfo.OwnerName = sp.Owner;
                targetObjectInfo.ObjectName = sp.Name;

                if (!csvDataList.Any(x => x.ObjectName == targetObjectInfo.ObjectName
                                       && x.OwnerName == targetObjectInfo.OwnerName))
                {
                    // CSVに該当データなし
                    continue;
                }

                // スクリプト情報を取得
                var sc = scrp.Script(new Urn[] { sp.Urn });

                // スクリプト作成
                CreateScript(db.Name, objectTypeName, targetObjectInfo, sc, appendFile);
            }
        }
        #endregion

        #region ユーザー定義関数のスクリプト作成
        /// <summary>
        /// ユーザー定義関数のスクリプト作成
        /// </summary>
        /// <param name="scrp">出力するスクリプトの設定</param>
        /// <param name="db">データベース</param>
        /// <param name="csvDataList">CSV情報</param>
        /// <param name="appendFile">追記指示</param>
        private static void CreateScriptUserDefinedFunction(Scripter scrp, Database db, List<CsvData> csvDataList, bool appendFile)
        {
            // F = ユーザー定義関数 の場合
            var objectTypeName = "UserDefinedFunction";

            foreach (UserDefinedFunction udf in db.UserDefinedFunctions)
            {
                if (udf.IsSystemObject)
                {
                    // システムオブジェクトの場合は、対象外
                    continue;
                }

                // 対象オブジェクト情報を設定
                var targetObjectInfo = new CsvData();
                targetObjectInfo.OwnerName = udf.Owner;
                targetObjectInfo.ObjectName = udf.Name;

                if (!csvDataList.Any(x => x.ObjectName == targetObjectInfo.ObjectName
                                       && x.OwnerName == targetObjectInfo.OwnerName))
                {
                    // CSVに該当データなし
                    continue;
                }

                // スクリプト情報を取得
                var sc = scrp.Script(new Urn[] { udf.Urn });

                // スクリプト作成
                CreateScript(db.Name, objectTypeName, targetObjectInfo, sc, appendFile);
            }
        }
        #endregion

        #region テーブルトリガーのスクリプト作成
        /// <summary>
        /// テーブルトリガーのスクリプト作成
        /// </summary>
        /// <param name="scrp">出力するスクリプトの設定</param>
        /// <param name="db">データベース</param>
        /// <param name="csvDataList">CSV情報</param>
        /// <param name="appendFile">追記指示</param>
        private static void CreateScriptTableTrigger(Scripter scrp, Database db, List<CsvData> csvDataList, bool appendFile)
        {
            // TT = テーブルトリガー の場合
            var objectTypeName = "TableTrigger";

            foreach(Table table in db.Tables)
            {
                foreach (Trigger tr in table.Triggers )
                {

                    // スクリプト情報を取得
                    StringCollection sc = null;

                    // 対象オブジェクト情報を設定
                    var targetObjectInfo = new CsvData();
                    targetObjectInfo.ObjectName = table.Owner;
                    targetObjectInfo.ObjectName = tr.Name;

                    if (tr.IsSystemObject)
                    {
                        // システムオブジェクトの場合は、対象外
                        continue;
                    }

                    if (!csvDataList.Any(x => x.ObjectName == targetObjectInfo.ObjectName))
                    {
                        // CSVに該当データなし
                        continue;
                    }

                    // スクリプト情報を設定
                    sc = scrp.Script(new Urn[] { tr.Urn });

                    if (null == sc)
                    {
                        // CSVに該当データなし
                        continue;
                    }

                    // スクリプト作成
                    CreateScript(db.Name, objectTypeName, targetObjectInfo, sc, appendFile);

                }

            }
        }
        #endregion

        #region トリガーのスクリプト作成
        /// <summary>
        /// トリガーのスクリプト作成
        /// </summary>
        /// <param name="scrp">出力するスクリプトの設定</param>
        /// <param name="db">データベース</param>
        /// <param name="csvDataList">CSV情報</param>
        /// <param name="appendFile">追記指示</param>
        private static void CreateScriptTrigger(Scripter scrp, Database db, List<CsvData> csvDataList, bool appendFile)
        {
            // T = トリガー の場合
            var objectTypeName = "Trigger";

            foreach (var tr in db.Triggers)
            {
                // 対象オブジェクト情報を設定
                var targetObjectInfo = new CsvData();

                // スクリプト情報を取得
                StringCollection sc = null;

                if (tr is DatabaseDdlTrigger)
                {
                    var ddlTrigger = tr as DatabaseDdlTrigger;

                    if (ddlTrigger.IsSystemObject)
                    {
                        // システムオブジェクトの場合は、対象外
                        continue;
                    }

                    targetObjectInfo.OwnerName = string.Empty;
                    targetObjectInfo.ObjectName = ddlTrigger.Name;

                    objectTypeName = "DdlTrigger";

                    // スクリプト情報を設定
                    sc = scrp.Script(new Urn[] { ddlTrigger.Urn });
                }

                if ((string.IsNullOrEmpty(targetObjectInfo.OwnerName)) && (string.IsNullOrEmpty(targetObjectInfo.ObjectName)))
                {
                    continue;
                }

                if (!csvDataList.Any(x => x.ObjectName == targetObjectInfo.ObjectName))
                {
                    // CSVに該当データなし
                    continue;
                }

                if (null == sc)
                {
                    // CSVに該当データなし
                    continue;
                }

                // スクリプト作成
                CreateScript(db.Name, objectTypeName, targetObjectInfo, sc, appendFile);
            }
        }
        #endregion

        #region スクリプト作成

        /// <summary>
        /// スクリプト作成
        /// </summary>
        /// <param name="dbName">DB名</param>
        /// <param name="objectTypeName">オブジェクトタイプ名</param>
        /// <param name="targetObjectInfo">対象オブジェクト情報</param>
        /// <param name="sc">スクリプト情報</param>
        　      /// <param name="appendFile">追記指示</param>
        /// <returns>実行結果(true:正常)</returns>
        private static bool CreateScript(string dbName, string objectTypeName, CsvData targetObjectInfo, StringCollection sc, bool appendFile)
        {
            var sbScriptData = new StringBuilder();
            
            sbScriptData.AppendFormat("/****** Object:  {3} [{0}].[{1}]  Script Date:{2} ******/"
                , targetObjectInfo.OwnerName
                , targetObjectInfo.ObjectName
                , System.DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")
                , objectTypeName
                );
            sbScriptData.AppendLine();

            foreach (var st in sc)
            {
                sbScriptData.AppendLine(st);
                sbScriptData.AppendLine("GO");
            }
            var txtScript = sbScriptData.ToString();
            if (string.IsNullOrEmpty(txtScript))
            {
                return false;
            }

            // 所有者があるオブジェクトのみ、所有者を設定する
            var ownerName = targetObjectInfo.OwnerName;
            if (!string.IsNullOrEmpty(ownerName))
            {
                ownerName += ".";
            }

            var outputPath = _pathAssembly + "\\"
                + string.Format("{0}{1}.{2}.sql", ownerName, targetObjectInfo.ObjectName, objectTypeName);

            //書き込むファイルが既に存在している場合は、上書きする
            using (var sw = new System.IO.StreamWriter(outputPath, appendFile))
            {
                sw.Write(txtScript);
            }

            _outputScriptCount++;

            return true;
        }

        #endregion
    }
}
