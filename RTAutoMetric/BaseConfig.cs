using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Contexts;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;

namespace RTAutoMetric
{
    public class BaseConfig<T>
    {
        private string key = "deepwise003chimingkueiasherasher";// 32 bytes key for AES-256
        public string config_path { get; set; }

        public BaseConfig() : this(@"Config.json")
        {
        }

        public BaseConfig(string _Path)
        {
            config_path = _Path;
        }

        ~BaseConfig()
        {
        }

        private void ConfigBackup()
        {
            string keyword = System.IO.Path.GetFileNameWithoutExtension(config_path) + "_Backup" + "*";
            // 判斷Config檔目錄是否根目錄
            string rootDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string absolutePath = System.IO.Path.GetFullPath(config_path);
            string[] files = absolutePath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase) ? Directory.GetFiles(rootDirectory, keyword) : Directory.GetFiles(Path.GetDirectoryName(config_path), keyword);
            foreach (string filePath in files)
            {
                File.Delete(filePath);
            }
            File.Copy(config_path, config_path.Split('.')[0] + "_Backup" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".json");
        }

        public void SaveInit(List<T> record, bool encryption = false)
        {
            string jsonData = JsonConvert.SerializeObject(record, Formatting.Indented);
            string dataToWrite = encryption ? AESEncrypt(jsonData) : jsonData;
            File.WriteAllText(config_path, dataToWrite);
        }

        public void Save(int model, int serialnumber, Object serialnumber_, bool encryption = false)
        {
            ConfigBackup();
            string jsonString_tmp = File.ReadAllText(config_path);
            string jsonString = encryption ? AESDecrypt(jsonString_tmp) : jsonString_tmp;
            JArray jsonArray = JArray.Parse(jsonString);
            JObject firstModel = (JObject)jsonArray[model]["Models"][serialnumber];
            JObject firstSequences = (JObject)firstModel["SerialNumbers"];
            // 使用迴圈將新的 SerialNumber 值設置到 JSON 中
            foreach (var property in serialnumber_.GetType().GetProperties())
            {
                firstSequences[property.Name] = property.GetValue(serialnumber_).ToString();
            }
            // 將修改後的 JSON 數據保存回文件
            string modifiedJsonString = encryption ? AESEncrypt(jsonArray.ToString()) : jsonArray.ToString();
            File.WriteAllText(config_path, modifiedJsonString);
        }

        public List<T> Load(bool encryption = false)
        {
            List<T> jsonData = null;
            if (File.Exists(config_path))
            {
                string Record = File.ReadAllText(config_path);
                jsonData = JsonConvert.DeserializeObject<List<T>>(encryption ? AESDecrypt(Record) : Record);
            }
            return jsonData;
        }

        public void Savecfg(string record)
        {
            using (StreamWriter writer = new StreamWriter("DW.cfg"))
            {
                writer.Write(record);
            }
        }

        public bool Loadcfg(out Tuple<int, int>tuple)
        {
            string filePath = "DW.cfg";
            bool state = false;
            tuple = null;
            if (File.Exists(filePath))
            {
                string text = null;
                using (StreamReader reader = new StreamReader(filePath))
                {
                    text = reader.ReadToEnd();
                }
                string[] parts = text.Split(',');
                if (parts.Length == 2)
                {
                    int firstValue = int.Parse(parts[0]);
                    int secondValue = int.Parse(parts[1]);
                    tuple = new Tuple<int, int>(firstValue, secondValue);
                    state = true;
                }
                else
                {
                    Console.WriteLine("請DW.cfg檔建立X,X格式!");
                }
            }
            else
            {
                Console.WriteLine("請建立DW.cfg檔。若軟體未建立參數，請略過此通知!");
            }
            return state;
        }

        // 加密
        public string AESEncrypt(string encryptStr)
        {
            byte[] keyArray = UTF8Encoding.UTF8.GetBytes(key);
            byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(encryptStr);
            RijndaelManaged rDel = new RijndaelManaged();
            rDel.Key = keyArray;
            rDel.Mode = CipherMode.ECB;
            rDel.Padding = PaddingMode.PKCS7;
            ICryptoTransform cTransform = rDel.CreateEncryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        }

        // 解密
        public string AESDecrypt(string decryptStr)
        {
            byte[] keyArray = UTF8Encoding.UTF8.GetBytes(key);
            byte[] toEncryptArray = Convert.FromBase64String(decryptStr);
            RijndaelManaged rDel = new RijndaelManaged();
            rDel.Key = keyArray;
            rDel.Mode = CipherMode.ECB;
            rDel.Padding = PaddingMode.PKCS7;
            ICryptoTransform cTransform = rDel.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
            return UTF8Encoding.UTF8.GetString(resultArray);
        }
    }



}
