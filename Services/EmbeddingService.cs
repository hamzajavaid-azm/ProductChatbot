using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using FastBertTokenizer;

namespace ProductChatbot.Services
{
    public class EmbeddingService
    {

        private readonly InferenceSession _session;
        private readonly BertTokenizer _tokenizer;
        public EmbeddingService(string modelPath, string vocabPath)
        {
            _session = new InferenceSession(modelPath);
            _tokenizer = new BertTokenizer();
            _tokenizer.LoadVocabularyAsync(vocabPath, convertInputToLowercase: true).GetAwaiter().GetResult();
        }
        public float[] GetEmbedding(string text)
        {
            var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Encode(text, maximumTokens: 256);
            int seqLen = inputIds.Length;

            var inputIdsTensor = new DenseTensor<long>(inputIds.ToArray(), new[] { 1, seqLen });
            var attentionMaskTensor = new DenseTensor<long>(attentionMask.ToArray(), new[] { 1, seqLen });
            var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds.ToArray(), new[] { 1, seqLen });

            var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

            using var results = _session.Run(inputs);
            var tokenEmbeddings = results.First(r => r.Name == "last_hidden_state").AsTensor<float>();

            int hiddenSize = tokenEmbeddings.Dimensions[2]; // 384 for this model
            var pooled = new float[hiddenSize];
            float maskSum = 0;

            for (int t = 0; t < seqLen; t++)
            {
                float m = attentionMask.Span[t];
                maskSum += m;
                for (int h = 0; h < hiddenSize; h++)
                    pooled[h] += tokenEmbeddings[0, t, h] * m;
            }

            for (int h = 0; h < hiddenSize; h++)
                pooled[h] /= maskSum;

            return pooled;
        }
    }
}
