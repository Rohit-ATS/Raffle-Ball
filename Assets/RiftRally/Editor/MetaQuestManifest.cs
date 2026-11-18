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
    /// - <c>com.oculus.supportedDevices</c> declares which headsets the app runs on. Unity's
    ///   own XR manifest library already writes one, and the list it writes begins with
    ///   <c>quest</c> - the original Quest, discontinued in 2023 - which is what the Horizon
    ///   upload validator complains about. So this is an override rather than an addition,
    ///   and it has to say so: two manifests declaring the same meta-data with different
    ///   values is a merge failure, not a last-one-wins.
    ///
    ///   The names are Meta's internal codenames and not the marketing ones: cambria is the
    ///   Quest Pro and eureka is the Quest 3. Writing "questpro|quest3" produces a manifest
    ///   that merges cleanly and matches no device at all.
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

            EnsureToolsNamespace(document);

            SetMetaData(document, application, "com.oculus.supportedDevices",
                        "quest2|cambria|eureka|quest3s");
            SetMetaData(document, application, "com.oculus.handtracking.frequency", "HIGH");
            SetMetaData(document, application, "com.oculus.handtracking.version", "V2.0");

            document.Save(manifestPath);
            Debug.Log("[RiftRally] Manifest: declared Quest 2 / Pro / 3 and high-frequency "
                      + "hand tracking.");
        }

        private const string ToolsNamespace = "http://schemas.android.com/tools";

        /// <summary>
        /// Declares the tools namespace on the manifest root, which has to exist before any
        /// <c>tools:</c> attribute written below it means anything.
        /// </summary>
        private static void EnsureToolsNamespace(XmlDocument document)
        {
            var manifest = document.SelectSingleNode("/manifest") as XmlElement;
            if (manifest == null) return;

            if (string.IsNullOrEmpty(manifest.GetAttribute("xmlns:tools")))
                manifest.SetAttribute("xmlns:tools", ToolsNamespace);
        }

        /// <summary>
        /// Adds a meta-data element, or updates it if this module already has one, and marks
        /// it as replacing whatever another module declares under the same name.
        ///
        /// Both halves are load-bearing. Appending a second element with a name this module
        /// already has produces a manifest that cannot merge with itself; omitting
        /// <c>tools:replace</c> produces one that cannot merge with Unity's XR library, which
        /// declares two of these three already. The failure arrives from Gradle as
        /// "Attribute ... is also present at ...", at the end of a twenty-minute build.
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
                SetReplaceMarker(document, node);
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

            SetReplaceMarker(document, element);
            application.AppendChild(element);
        }
        /// <summary>
        /// Marks an element as overriding the same element from another module, which is how
        /// the Android manifest merger is told that a conflict is deliberate.
        /// </summary>
        private static void SetReplaceMarker(XmlDocument document, XmlNode node)
        {
            if (node.Attributes == null) return;

            XmlAttribute replace = node.Attributes["replace", ToolsNamespace];
            if (replace == null)
            {
                replace = document.CreateAttribute("tools", "replace", ToolsNamespace);
                node.Attributes.Append(replace);
            }

            replace.Value = "android:value";
        }
    }
}
