using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using XIVLauncher.Common.Util;

namespace XIVLauncher.Common.Game
{
	public class SdoAreas
	{
		[JsonProperty("servers")]
		public SdoArea[] Servers { get; set; }
		public static async Task<SdoAreas> Get()
		{
			var request = new HttpRequestMessage(HttpMethod.Get, "https://ff.dorado.sdo.com/ff/area/serverlist_new.js");
			request.Headers.AddWithoutValidation("Accept", "*/*");
			request.Headers.AddWithoutValidation("Host", "ff.dorado.sdo.com");
			var client = new HttpClient();
			var resp = await client.SendAsync(request);
			var text = await resp.Content.ReadAsStringAsync();
			var json = text.Trim();
			json = json.Substring("var servers=".Length);
			json = json.Substring(0, json.Length - 1);
			json = $"{{\"servers\":{json}}}";
			//Console.WriteLine(json);
			return  JsonConvert.DeserializeObject<SdoAreas>(json); ;
		}
	}
	public class SdoArea {
		//"Areaid":"1",
		public string Areaid { get; set; }
		//"AreaStat":1,
		public int AreaStat { get; set; }
		//"AreaOrder":4,
		public int AreaOrder { get; set; }
		//"AreaName":"陆行鸟",
		public string AreaName { get; set; }
		//"Areatype":1,
		public int Areatype { get; set; }
		//"AreaLobby":"ffxivlobby01.ff14.sdo.com",
		public string AreaLobby { get; set; }
		//"AreaGm":"ffxivgm01.ff14.sdo.com",
		public string AreaGm { get; set; }
		//"AreaPatch":"ffxivpatch01.ff14.sdo.com",
		public string AreaPatch { get; set; }
		//"AreaConfigUpload":"ffxivsdb01.ff14.sdo.com"
		public string AreaConfigUpload { get; set; }
	}
}
