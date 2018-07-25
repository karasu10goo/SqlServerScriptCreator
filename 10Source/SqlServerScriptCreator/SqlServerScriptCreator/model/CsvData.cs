using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServerScriptCreator
{
    /// <summary>
    /// CSVファイルの内容
    /// </summary>
    internal class CsvData
    {
        #region Fields

        /// <summary>
        /// 所有者
        /// </summary>
        private string _ownerName;

        /// <summary>
        /// オブジェクト名
        /// </summary>
        private string _objectName;

        #endregion

        /// <summary>
        /// デフォルトコンストラクタ。
        /// </summary>
        internal CsvData()
        {
        }

        /// <summary>
        /// 所有者
        /// </summary>
        public string OwnerName
        {
            get
            {
                return this._ownerName;
            }

            set
            {
                this._ownerName = value;
            }
        }

        /// <summary>
        /// オブジェクト名
        /// </summary>
        public string ObjectName
        {
            get
            {
                return this._objectName;
            }

            set
            {
                this._objectName = value;
            }
        }
    }
}
