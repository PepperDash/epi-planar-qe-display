using Newtonsoft.Json;

namespace Pepperdash.Essentials.Plugins.Display.Planar.Qe
{
	public class PlanarQePropertiesConfig
	{
		/// <summary>
		/// Poll interval in miliseconds, defaults 45,000ms (45-seconds)
		/// </summary>
		[JsonProperty("pollIntervalMs")]
		public long PollIntervalMs { get; set; }

		/// <summary>
		/// Device cooling time, defaults to 15,000ms (15-seconds)
		/// </summary>
		[JsonProperty("coolingTimeMs")]
		public uint CoolingTimeMs { get; set; }

		/// <summary>
		/// Device warming time, defaults to 15,000ms (15-seconds)
		/// </summary>
		[JsonProperty("warmingTimeMs")]
		public uint WarmingTimeMs { get; set; }

		/// <summary>
		/// Supports USB input, defaults to false
		/// If true, a USB input port will be added to the device
		/// </summary>
		[JsonProperty("supportsUsb")]
		public bool SupportsUsb { get; set; }
	}
}