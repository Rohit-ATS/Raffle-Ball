using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace RiftRally.EditorTools
{
    /// <summary>
    /// Writes the two Meta-specific entries into the generated Android manifest that Unity
    /// does not add by itself, and that the Horizon upload validator checks for.
    ///
    /// Both of these are rejections that only appear at the far end of an upload, after a
    /// twenty-minute IL2CPP build - which is exactly the sort of thing that should be a
    /// property of the build rather than something a person remembers to do:
    ///
    /// - <c>com.oculus.supportedDevices</c> declares which headsets the app runs on. Without
    ///   it the store assumes every device ever made, including the original Quest, which has
    ///   not been supported since 2023 and which this game could not hold 90 Hz on anyway.
    /// - <c>com.oculus.handtracking.frequency</c> asks for the high-frequency hand tracking
    ///   path. This game is a throwing game read entirely from pinch position and release
    ///   velocity, so the tracking rate is not a nicety - at the low rate the release
    ///   velocity is sampled too coarsely and throws come out weaker than they felt.
    ///
    /// This runs after Unity generates the Gradle project and before it builds, which is the
    /// only point where the real manifest exists and is still editable.
    /// </summary>
    public class MetaQuestManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1;

        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[RiftRally] No generated manifest at {manifestPath}; "
                                 + "the Meta device declarations were not added.");
                return;
            }

            var document = new XmlDocument();
            document.Load(manifestPath);

            XmlNode application = document.SelectSingleNode("/manifest/application");
            if (application == null)
            {
                Debug.LogWarning("[RiftRally] The generated manifest has no <application> "
                                 + "element; the Meta device declarations were not added.");
                return;
            }

            SetMetaData(document, application, "com.oculus.supportedDevices",
                        "quest2|questpro|quest3");
            SetMetaData(document, application, "com.oculus.handtracking.frequency", "HIGH");
            SetMetaData(document, application, "com.oculus.handtracking.version", "V2.0");

            document.Save(manifestPath);
            Debug.Log("[RiftRally] Manifest: declared Quest 2 / Pro / 3 and high-frequency "
                      + "hand tracking.");
        }

        /// <summary>
        /// Adds a meta-data element, or updates it if the build already has one. Updating
        /// rather than appending matters: a manifest with the same meta-data name twice fails
        /// to merge, and a second build would otherwise produce one.
        /// </summary>
        private static void SetMetaData(XmlDocument document, XmlNode application,
                                        string name, string value)
        {
            foreach (XmlNode node in application.ChildNodes)
            {
                if (node.Name != "meta-data" || node.Attributes == null) continue;

                XmlAttribute existing = node.Attributes["name", AndroidNamespace];
                if (existing == null || existing.Value != name) continue;

                XmlAttribute valueAttribute = node.Attributes["value", AndroidNamespace];
                if (valueAttribute == null)
                {
                    valueAttribute = document.CreateAttribute("android", "value",
                                                              AndroidNamespace);
                    node.Attributes.Append(valueAttribute);
                }

                valueAttribute.Value = value;
                return;
            }

            XmlElement element = document.CreateElement("meta-data");

            XmlAttribute nameAttribute = document.CreateAttribute("android", "name",
                                                                  AndroidNamespace);
            nameAttribute.Value = name;
            element.Attributes.Append(nameAttribute);

            XmlAttribute added = document.CreateAttribute("android", "value", AndroidNamespace);
            added.Value = value;
            element.Attributes.Append(added);

            application.AppendChild(element);
        }
    }
}
