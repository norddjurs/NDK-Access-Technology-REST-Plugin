using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using RestSharp;
using RestSharp.Deserializers;
using RestSharp.Serializers;

namespace NDK.AcctPlugin {
	/// <summary>
	/// Serializes an object using the DataContractSerializer
	/// </summary>
	public class XmlDataContractSerializer : ISerializer, IDeserializer {
		protected virtual DataContractSerializer CreateSerializer(object obj) {
			return new DataContractSerializer(obj.GetType());
		}

		public string Serialize(object obj) {
			var serializer = CreateSerializer(obj);
			using (var writer = new StringWriter()) {
				using (var xwriter = new XmlTextWriter(writer)) {
					serializer.WriteObject(xwriter, obj);
					xwriter.Flush();
					xwriter.Close();
				}
				return writer.ToString();
			}
		}

		public T Deserialize<T>(IRestResponse response) {
			return (T)new DataContractSerializer(typeof(T)).ReadObject(XmlReader.Create(new StringReader(response.Content)));
		}

		public string RootElement {
			get {
				return null;
			}
			set { }
		}
		public string Namespace {
			get {
				return null;
			}
			set { }
		}
		public string DateFormat {
			get {
				return null;
			}
			set { }
		}

		private string _ct = "text/xml";
		public string ContentType { get { return _ct; } set { _ct = value; } }
	}

	/*
		private static Encoding _defaultEncoding = Encoding.UTF8;

			/// <summary>
			/// The configuration used to create an XML writer
			/// </summary>
			protected XmlWriterSettings XmlWriterSettings { get; set; }

			/// <summary>
			/// Constructor
			/// </summary>
			public XmlDataContractSerializer()
			{
				XmlWriterSettings = new System.Xml.XmlWriterSettings
				{
					Encoding = _defaultEncoding,
				};
			}

			/// <summary>
			/// Serialize the object into a byte array
			/// </summary>
			/// <param name="obj">Object to serialize</param>
			/// <returns>Byte array to send in the request body</returns>
			public byte[] Serialize(object obj)
			{
				var serializer = CreateSerializer(obj);
				using (var temp = new MemoryStream())
				{
					using (var writer = System.Xml.XmlWriter.Create(temp, XmlWriterSettings))
					{
						serializer.WriteObject(temp, obj);
					}
					var result = temp.ToArray();
					return result;
				}
			}

			private MediaTypeHeaderValue _defaultContentType;
			private MediaTypeHeaderValue _contentType;

			/// <summary>
			/// Content type produced by the serializer
			/// </summary>
			/// <remarks>
			/// As long as there is no manually set content type, the content type character set will always reflect the encoding
			/// of the XmlWriterSettings.
			/// </remarks>
			public MediaTypeHeaderValue ContentType
			{
				get
				{
					if (_contentType == null)
					{
						if (_defaultContentType == null || _defaultContentType.CharSet != XmlWriterSettings.Encoding.WebName)
						{
							_defaultContentType = new MediaTypeHeaderValue("text/xml")
							{
								CharSet = XmlWriterSettings.Encoding.WebName,
							};
						}
						return _defaultContentType;
					}
					return _contentType;
				}
				set
				{
					_contentType = value;
				}
			}

			/// <summary>
			/// Create a new data contract serializer
			/// </summary>
			/// <param name="obj">The object to create the serializer for</param>
			protected virtual DataContractSerializer CreateSerializer(object obj)
			{
				return new DataContractSerializer(obj.GetType());
			}
		}
	 */

} // NDK.AcctPlugin