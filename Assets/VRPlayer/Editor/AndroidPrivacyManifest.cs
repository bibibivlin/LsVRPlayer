using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace VRPlayer.Editor
{
    // PICO adds its default permissions during Gradle generation; run after that callback.
    public sealed class AndroidPrivacyManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder=>int.MaxValue;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file=Path.Combine(path,"src/main/AndroidManifest.xml");
            var document=new XmlDocument();document.Load(file);
            Apply(document);document.Save(file);
        }
        public static void Apply(XmlDocument document)
        {
            const string android="http://schemas.android.com/apk/res/android";
            const string tools="http://schemas.android.com/tools";
            var manifest=document.DocumentElement;
            manifest.SetAttribute("xmlns:tools",tools);
            foreach(string name in new[]{"android.permission.WRITE_SETTINGS","com.pvr.tobactivate.permission.AUTH_CHECK"}){
                XmlElement entry=null;
                foreach(XmlNode node in manifest.SelectNodes("uses-permission"))
                    if(node is XmlElement element&&element.GetAttribute("name",android)==name){entry=element;break;}
                if(entry==null){entry=document.CreateElement("uses-permission");entry.SetAttribute("name",android,name);manifest.AppendChild(entry);}
                // Also reject the same permission when it is contributed by an AAR.
                entry.SetAttribute("node",tools,"remove");
            }
            var application=(XmlElement)manifest.SelectSingleNode("application");
            application.SetAttribute("allowBackup",android,"false");
            application.SetAttribute("fullBackupContent",android,"false");
            string replace=application.GetAttribute("replace",tools);
            foreach(string attribute in new[]{"android:allowBackup","android:fullBackupContent"})
                if(!(","+replace+",").Contains(","+attribute+","))replace=string.IsNullOrEmpty(replace)?attribute:replace+","+attribute;
            application.SetAttribute("replace",tools,replace);
        }
    }
}
