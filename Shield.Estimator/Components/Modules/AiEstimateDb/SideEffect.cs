//Ai.cs

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb
{
    public class SideEffect
    {
        // побочка от изменения параметров модели (в ответе спрашивает => Let me know if you have any other...)
        public static async Task<string> DeleteUnnecessary(string text)
        {
            await Task.Delay(0);
            int startIndex1 = text.IndexOf("Let me");
            int startIndex2 = text.IndexOf("```python");
            if (startIndex1 != -1)
            {
                text = text.Remove(startIndex1, text.Length - startIndex1);
            }
            if (startIndex2 != -1)
            {
                text = text.Remove(startIndex2, text.Length - startIndex2);
            }
            text = text.Replace("\n\n", "\n").TrimStart('.').Trim();
            return text;
        }

    }

}