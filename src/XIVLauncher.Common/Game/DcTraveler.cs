using Serilog;
using System;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using XIVLauncher.Common.Addon;
using XIVLauncher.Common.Http;

namespace XIVLauncher.Common.Game
{
    public class DcTraveleApiException : Exception
    {
        public bool IsNetworkTimeout { get; set; } = false;
        public DcTraveleApiException(string message, bool isNetworkTimeout = false) : base(message)
        {
            this.IsNetworkTimeout = isNetworkTimeout;
        }
    }
    public class DcTraveler
    {
        private readonly HttpClient httpClient;
        private readonly CookieContainer cookieContainer;
        private const string BaseUrl = "ff14bjz.sdo.com";
        private const string Domain = "sdo.com";
        private string ticket = string.Empty;
        private readonly Func<Task<string>> refreshGameSessionIdFunc;
        private readonly Func<Task<string>> refreshDcTravelSessionIdFunc;
        private bool isInitialized = false;
        public DcTraveler(string nSessionId, Func<Task<string>> refreshGameSessionIdFunc, Func<Task<string>> refreshDcTravelSessionIdFunc)
        {
            this.refreshDcTravelSessionIdFunc = refreshDcTravelSessionIdFunc;
            this.refreshGameSessionIdFunc = refreshGameSessionIdFunc;

            this.cookieContainer = new CookieContainer();
            if (!string.IsNullOrEmpty(nSessionId))
            {
                this.cookieContainer.Add(new Cookie("nsessionid", nSessionId, "/", Domain));
            }
            this.cookieContainer.Add(new Cookie("CAS_LOGIN_STATE", "1", "/", Domain));
            this.cookieContainer.Add(new Cookie("SECURE_CAS_LOGIN_STATE", "1", "/", Domain));
            this.cookieContainer.Add(new Cookie("isLogin", "1", "/", Domain));

            var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            this.httpClient = new HttpClient(handler);
            var headers = new Dictionary<string, string>() {
                { "Accept", "application/json" },
                { "Accept-Encoding", "gzip, deflate, br, zstd" },
                { "Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6" },
                { "Content-Type", "application/json" },
                { "Priority", "u=1, i" },
                { "Sec-Ch-Ua", "\"Microsoft Edge\";v=\"137\", \"Chromium\";v=\"137\", \"Not/A)Brand\";v=\"24\"" },
                { "Sec-Ch-Ua-Mobile", "?0" },
                { "Sec-Ch-Ua-Platform", "\"Windows\"" },
                { "Sec-Fetch-Dest", "empty" },
                { "Sec-Fetch-Mode", "cors" },
                { "Sec-Fetch-Site", "same-origin" },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36 Edg/137.0.0.0" }
            };
            foreach (var header in headers)
            {
                this.httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        [HttpRpc]
        public async Task<string> RefreshGameSessionId()
        {
            return await this.refreshGameSessionIdFunc();
        }

        #region 初始化 认证
        public async Task GetValidCookie()
        {
            if (await this.InitTravelPage())
            {
                Log.Information("[DcTravel] Successfully initialized travel page.");
                isInitialized = true;
            }
            else
            {
                Log.Error("[DcTravel] Failed to initialize travel page. Need valid ticket");
                this.ticket = await this.refreshDcTravelSessionIdFunc!.Invoke();
                await ValidateTicket();
                if (await this.InitTravelPage())
                {
                    Log.Information("[DcTravel] Successfully initialized travel page.");
                    isInitialized = true;
                }
            }
        }

        public string GetNSessionIdFromCookie()
        {
            var cookies = cookieContainer.GetCookies(new Uri($"https://{BaseUrl}"));
            var nSessionId = cookies.First(x => x.Name == "nsessionid").Value;
            return nSessionId;
        }

        public async Task<bool> InitTravelPage()
        {
            //https://ff14bjz.sdo.com/api/orderserivce/pageInit?migrationType=4
            try
            {
                _ = await GetRequestData("api/orderserivce/pageInit", ApiType.TravelWithTicket, new Dictionary<string, string>() { { "migrationType", "4" } }, true);
                return true;
            }
            catch (DcTraveleApiException ex)
            {
                Log.Error(ex, "Failed to initialize travel page");
                return false;
            }
        }
        public async Task ValidateTicket()
        {
            //https://ff14bjz.sdo.com/api/gmallinter/validateTicket?ticket=ULS21-000000000000000000000000
            _ = await GetRequestData("api/gmallinter/validateTicket", ApiType.TravelWithTicket, new Dictionary<string, string>() { { "ticket", this.ticket } }, true);
        }
        #endregion

        #region 公共请求
        private void EnsureReturnCode(JsonNode node)
        {
            var returnCode = 0;
            if (node == null || (returnCode = node["return_code"].GetValue<int>()) != 0)
            {
                throw new DcTraveleApiException($"API call failed with return code: {node?["return_code"]?.GetValue<int>()}, message: {node?["return_message"]?.GetValue<string>()}", returnCode == -10339000);
            }
        }


        private enum ApiType
        {
            Travel,
            TravelWithTicket,
            Order,
        }

        private async Task<JsonNode> GetRequestData(string api, ApiType type, Dictionary<string, string> parameters = null, bool ignoreInitialized = false)
        {
            if (!ignoreInitialized && !isInitialized)
            {
                throw new DcTraveleApiException("DcTraveler is not initialized. Please call GetValidCookie() first.");
            }
            //{"return_code":-10339000,"return_message":"网络超时，请稍后重试！","data":{}}
            var requestUri = new Uri($"https://{BaseUrl}/{api}");
            var uriBuilder = new UriBuilder(requestUri);
            var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);
            if (parameters != null && parameters.Count != 0)
            {
                foreach (var item in parameters)
                {
                    queryParams.Add(item.Key, HttpUtility.UrlEncode(item.Value));
                }
            }
            uriBuilder.Query = queryParams.ToString();
            using (var request = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri))
            {
                switch (type)
                {
                    case ApiType.Travel:
                        request.Headers.Add("Refer", "https://ff14bjz.sdo.com/RegionKanTelepo");
                        break;
                    case ApiType.TravelWithTicket:
                        request.Headers.Add("Refer", $"https://ff14bjz.sdo.com/RegionKanTelepo?ticket={this.ticket}");
                        break;
                    case ApiType.Order:
                        request.Headers.Add("Refer", "https://ff14bjz.sdo.com/orderList");
                        break;
                }
                var tryNum = 3;
                while (tryNum-- > 0)
                {
                    try
                    {
                        Log.Debug($"[DcTravel] request: {uriBuilder.Uri}");
                        var response = await this.httpClient.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                        var content = await response.Content.ReadAsStringAsync();
                        Log.Debug($"[DcTravel] response: {content}");
                        var node = JsonNode.Parse(content);
                        EnsureReturnCode(node);
                        return node["data"];
                    }
                    catch (Exception ex)
                    {
                        if (ex is DcTraveleApiException dcEx && dcEx.IsNetworkTimeout)
                        {
                            await Task.Delay(5000);
                            continue;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }
            throw new DcTraveleApiException("Failed to get request data after multiple attempts.");
        }
        #endregion

        #region 查询传送页面
        public class Area
        {
            [JsonPropertyName("areaId")]
            public int AreaId { get; set; }
            [JsonPropertyName("areaName")]
            public string AreaName { get; set; }
            [JsonPropertyName("groups")]
            public List<Group> GroupList { get; set; }
            public void SetAreaForGroup()
            {
                foreach (var group in GroupList)
                {
                    group.Area = this;
                }
            }
        }
        public class Group
        {
            [JsonIgnore]
            public Area Area { get; set; }
            [JsonPropertyName("groupId")]
            public int GroupId { get; set; }
            [JsonPropertyName("amount")]
            public int Amount { get; set; }
            [JsonPropertyName("groupName")]
            public string GroupName { get; set; }
            [JsonPropertyName("queueTime")]
            public int? QueueTime { get; set; }
            [JsonPropertyName("groupCode")]
            public string GroupCode { get; set; }
        }
        [HttpRpc]
        public async Task<List<Area>> QueryGroupListTravelSource()
        {
            //https://ff14bjz.sdo.com/api/orderserivce/queryGroupListTravelSource?appId=100001900
            var data = await GetRequestData("api/orderserivce/queryGroupListTravelSource", ApiType.Travel, new Dictionary<string, string>() { { "appId", "100001900" } });
            if (data["resultCode"].GetValue<int>() != 0)
                throw new DcTraveleApiException($"Failed to query group list travel source, resultCode: {data["resultCode"].GetValue<int>()}, message: {data["resultMessage"].GetValue<string>()}");
            var areaList = JsonSerializer.Deserialize<List<Area>>(JsonNode.Parse(data["groupList"].GetValue<string>()));
            foreach (var item in areaList)
            {
                item.SetAreaForGroup(); 
            }
            return areaList;
        }

        [HttpRpc]
        public async Task<List<Area>> QueryGroupListTravelTarget(int areaId, int groupId)
        {
            //https://ff14bjz.sdo.com/api/orderserivce/queryGroupListTravelTarget?appId=100001900&areaId=7&groupId=5
            var data = await GetRequestData("api/orderserivce/queryGroupListTravelTarget", ApiType.Travel, new Dictionary<string, string>() { { "appId", "100001900" }, { "areaId", $"{areaId}" }, { "groupId", $"{"groupId"}" } });
            if (data["resultCode"].GetValue<int>() != 0)
                throw new DcTraveleApiException($"Failed to query group list travel target, resultCode: {data["resultCode"].GetValue<int>()}, message: {data["resultMessage"].GetValue<string>()}");
            var areaList = JsonSerializer.Deserialize<List<Area>>(JsonNode.Parse(data["groupList"].GetValue<string>()));
            foreach (var item in areaList)
            {
                item.SetAreaForGroup();
            }
            return areaList;
        }

        public class Character
        {
            [JsonPropertyName("roleId")]
            public string ContentId { get; set; }
            [JsonPropertyName("roleName")]
            public string Name { get; set; }
            public int AreaId { get; set; }
            public int GroupId { get; set; }
            public string ToQueryString()
            {
                // Shit!
                return $"{{\"roleId\":\"{ContentId}\",\"roleName\":\"{Name}\",\"key\":0}}";
            }
        }
        [HttpRpc]
        public async Task<List<Character>> QueryRoleList(int areaId, int groupId)
        {
            //https://ff14bjz.sdo.com/api/gmallgateway/queryRoleList4Migration?appId=100001900&areaId=7&groupId=5
            var data = await GetRequestData("api/gmallgateway/queryRoleList4Migration", ApiType.Travel, new Dictionary<string, string>() { { "appId", "100001900" }, { "areaId", $"{areaId}" }, { "groupId", $"{groupId}" } });
            if (data["resultCode"].GetValue<int>() != 0)
                throw new DcTraveleApiException($"Failed to query role list, resultCode: {data["resultCode"].GetValue<int>()}, message: {data["resultMessage"].GetValue<string>()}");
            var characterList = JsonSerializer.Deserialize<List<Character>>(JsonNode.Parse(data["roleList"].GetValue<string>()));
            foreach (var character in characterList)
            {
                character.AreaId = areaId;
                character.GroupId = groupId;
            }
            return characterList;
        }
        [HttpRpc]
        public async Task<int> QueryTravelQueueTime(int areaId, int groupId)
        {
            //https://ff14bjz.sdo.com/api/orderserivce/travelQueueTime?appId=100001900&migrationType=4&targetArea=8&targetGroupId=1
            var data = await GetRequestData("api/gmallgateway/queryRoleList4Migration", ApiType.Travel, new Dictionary<string, string>() { { "appId", "100001900" }, { "migrationType", "4" }, { "areaId", $"{areaId}" }, { "groupId", $"{"groupId"}" } });
            if (data["resultCode"].GetValue<int>() != 0)
                throw new DcTraveleApiException($"Failed to query travel queue time, resultCode: {data["resultCode"].GetValue<int>()}, message: {data["resultMessage"].GetValue<string>()}");
            return data["minutes"].GetValue<int>();
        }
        [HttpRpc]
        public async Task<string> TravelOrder(Group targetGroup, Group sourceGroup, Character character)
        {
            //https://ff14bjz.sdo.com/api/orderserivce/travelOrder?appId=100001900&areaId=7&areaName=%E7%8C%AB%E5%B0%8F%E8%83%96&groupId=5&groupCode=HaiMaoChaWu&groupName=%E6%B5%B7%E7%8C%AB%E8%8C%B6%E5%B1%8B&productId=1&productNum=1&migrationType=4&targetArea=8&targetAreaName=%E8%B1%86%E8%B1%86%E6%9F%B4&targetGroupId=1&targetGroupCode=ShuiJingTa2&targetGroupName=%E6%B0%B4%E6%99%B6%E5%A1%94&roleList=%5b%7b%22roleId%22%3a%22114514%22%2c%22roleName%22%3a%22%e4%b8%9d%e7%93%9c%e5%8d%a1%e5%a4%ab%e5%8d%a1%22%2c%22key%22%3a0%7d%5d&isMigrationTimes=0
            //https://ff14bjz.sdo.com/api/orderserivce/travelOrder?
            //appId=100001900&isMigrationTimes=0&productId=1&productNum=1&migrationType=4&
            //areaId=7&areaName=猫小胖&groupId=5&groupCode=HaiMaoChaWu&groupName=海猫茶屋
            //targetArea=8&targetAreaName=豆豆柴&targetGroupId=1&targetGroupCode=ShuiJingTa2&targetGroupName=水晶塔&
            //roleList=[{"roleId":"114514","roleName":"丝瓜卡夫卡","key":0}]&
            var data = await GetRequestData(
                "api/gmallgateway/queryRoleList4Migration",
                ApiType.Travel,
                new Dictionary<string, string>() {
                    { "appId", "100001900" },{ "migrationType", "4" },{ "isMigrationTimes", "1" },{ "productId", "1"} ,
                    { "areaId", $"{sourceGroup.Area.AreaId}" },{ "areaName", $"{sourceGroup.Area.AreaName}" },{ "groupId", $"{sourceGroup.GroupId}" },{ "groupCode", $"{sourceGroup.GroupCode}" },{ "groupName", $"{sourceGroup.GroupName}" },
                    { "targetArea", $"{targetGroup.Area.AreaId}" },{ "targetAreaName", $"{targetGroup.Area.AreaName}" },{ "targetGroupId", $"{targetGroup.GroupId}" },{ "targetGroupCode", $"{targetGroup.GroupCode}" },{ "targetGroupName", $"{targetGroup.GroupName}" },
                    { "roleList", $"[{character.ToQueryString()}]"}
                });
            return data["orderId"].GetValue<string>();
        }

        public enum MigrationStatus
        {
            Failed = -1,
            InPrepare = 0,
            InQueue = 4,
            Completed = 5,
        }
        [HttpRpc]
        public async Task<MigrationStatus> QueryOrderStatus(string orderId)
        {
            //https://ff14bjz.sdo.com/api/gmallgateway/queryOrderStatus?orderId=GM017624122025062800161000001006
            var data = await GetRequestData("api/gmallgateway/queryOrderStatus", ApiType.Travel, new Dictionary<string, string>() { { "orderId", orderId } });
            var migrationStatus = (MigrationStatus)data["migrationStatus"].GetValue<int>();
            return migrationStatus;
        }
        #endregion

        #region 订单页面
        [HttpRpc]
        public async Task<bool> InitOrderPage()
        {
            //https://ff14bjz.sdo.com/api/orderserivce/pageInit?migrationType=0
            try
            {
                _ = await GetRequestData("api/orderserivce/pageInit", ApiType.Order, new Dictionary<string, string>() { { "migrationType", "0" } });
                return true;
            }
            catch (DcTraveleApiException ex)
            {
                Log.Error(ex, "Failed to initialize order page");
                return false;
            }
        }
        public enum OrderStatus
        {
            Failed,
            Arrival,
            Completed,
            Backing,
            Backed,
            Unknown
        }
        public class MigrationOrder
        {
            [JsonPropertyName("orderId")]
            public string OrderId { get; set; }
            [JsonPropertyName("roleId")]
            public string ContentId { get; set; }
            [JsonPropertyName("groupId")]
            public int GroupId { get; set; }
            [JsonPropertyName("groupCode")]
            public string GroupCode { get; set; }
            [JsonPropertyName("groupName")]
            public string GroupName { get; set; }
            [JsonIgnore]
            public OrderStatus Status { get; set; }
            [JsonPropertyName("createTime")]
            public string CreateTime { get; set; }
        }

        [HttpRpc]
        public async Task<List<MigrationOrder>> QueryMigrationOrders(int pageIndex = 1)
        {
            //https://ff14bjz.sdo.com/api/orderserivce/queryMigrationOrders?appId=100001900&pageIndex=1&pageNum=10
            var data = await GetRequestData("api/orderserivce/queryMigrationOrders", ApiType.Order, new Dictionary<string, string>() { { "appId", "100001900" }, { "pageIndex", $"{pageIndex}" }, { "pageNum", $"10" } });
            if (data["resultCode"].GetValue<int>() != 0)
                throw new DcTraveleApiException($"Failed to query order list, resultCode: {data["resultCode"].GetValue<int>()}, message: {data["resultMessage"].GetValue<string>()}");
            var orderList = new List<MigrationOrder>();
            var orderListArray = JsonNode.Parse(data["orderlist"].GetValue<string>()) as JsonArray;

            foreach (var order in orderListArray)
            {
                var orderId = order["orderId"].GetValue<string>();
                var roleId = order["migrationDetailList"][0]["roleId"].GetValue<string>();
                var groupId = order["groupId"].GetValue<int>();
                var groupCode = order["groupCode"].GetValue<string>();
                var groupName = order["groupName"].GetValue<string>();
                var migrationStatus = (MigrationStatus)order["migrationStatus"].GetValue<int>();
                var migrationType = order["migrationType"].GetValue<int>();
                var travelStatus = order["travelStatus"].GetValue<int>();
                var createTime = order["createTime"].GetValue<string>();
                var orderStatus = OrderStatus.Unknown;
                if (migrationStatus == MigrationStatus.Failed)
                {
                    orderStatus = OrderStatus.Failed;
                }
                else if (migrationType == 5 && travelStatus == 1 && migrationStatus == MigrationStatus.Completed)
                {
                    orderStatus = OrderStatus.Backed;
                }
                else if (migrationType == 4 && travelStatus == 1 && migrationStatus == MigrationStatus.Completed)
                {
                    orderStatus = OrderStatus.Arrival;
                }
                else if (migrationType == 4 && travelStatus == 3 && migrationStatus == MigrationStatus.Completed)
                {
                    orderStatus = OrderStatus.Completed;
                }
                else if (migrationType == 5 && travelStatus == 1 && migrationStatus == MigrationStatus.InPrepare)
                {
                    orderStatus = OrderStatus.Backing;
                }
                else
                {
                    var jsonText = order.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    Log.Error($"[DcTravel] Unknown order statue:\n{jsonText}");
                }
                orderList.Add(new MigrationOrder
                {
                    OrderId = orderId,
                    ContentId = roleId,
                    GroupId = groupId,
                    GroupCode = groupCode,
                    GroupName = groupName,
                    Status = orderStatus,
                    CreateTime = createTime
                });
            }
            return orderList;
        }
        [HttpRpc]
        public async Task TravelBack(MigrationOrder order)
        {
            //https://ff14bjz.sdo.com/api/orderserivce/travelBack?travelOrderId=GM017624122025062800161000001006&groupId=23&groupCode=ShenYiZhiDi&groupName=%E7%A5%9E%E6%84%8F%E4%B9%8B%E5%9C%B0
            var data = await GetRequestData("api/orderserivce/travelBack", ApiType.Order, new Dictionary<string, string>() { { "travelOrderId", order.OrderId }, { "groupId", $"{order.GroupId}" }, { "groupCode", $"{order.GroupCode}" }, { "groupName", $"{order.GroupName}" } });
            if (data["resultCode"].GetValue<int>() != 0)
                throw new DcTraveleApiException($"Failed to travel back, resultCode: {data["resultCode"].GetValue<int>()}, message: {data["resultMessage"].GetValue<string>()}");
        }
        #endregion
    }
}
