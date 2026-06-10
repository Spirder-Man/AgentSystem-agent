
using System;
using System.Collections.Generic;

namespace Agent1.Services
{
    public class RetrievedChunk
    {
        public string Id { get; set; }
        public string Content { get; set; }
        public double Score { get; set; }
        public int Rank { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public string RetrievalMethod { get; set; }

        public RetrievedChunk()
        {
            Id = Guid.NewGuid().ToString();
            Content = string.Empty;
            Metadata = new Dictionary<string, object>();
            RetrievalMethod = "BM25";
        }

        public static RetrievedChunk Create(string content, double score, int rank, Dictionary<string, object>? metadata = null)
        {
            return new RetrievedChunk
            {
                Content = content,
                Score = score,
                Rank = rank,
                Metadata = metadata ?? new Dictionary<string, object>()
            };
        }

        public override string ToString()
        {
            string content = Content ?? "";
            string shortContent = content.Length > 50 ? content.Substring(0, 50) + "..." : content;
            return $"[Rank {Rank}] Score: {Score:F4} | Content: {shortContent}";
        }
    }
}

