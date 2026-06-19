using System.Text;
using Newtonsoft.Json.Linq;

namespace ChatBot
{
    public class OpenRouterApiClient
    {
        private HttpClient client = new HttpClient();

        public OpenRouterApiClient(string apiKey)
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/GitPolyakoff");
            client.DefaultRequestHeaders.Add("X-Title", "TruckersMP ChatBot");
        }

        public async Task<JObject> CreateChatCompletionAsync(JObject request)
        {
            StringContent httpContent = new StringContent(request.ToString(), Encoding.UTF8, "application/json");

 
            HttpResponseMessage response = await client.PostAsync("https://openrouter.ai/api/v1/chat/completions", httpContent);
            response.EnsureSuccessStatusCode();
            string responseString = await response.Content.ReadAsStringAsync();

            return JObject.Parse(responseString);
        }
    }
}