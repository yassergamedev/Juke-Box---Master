using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDBModels
{
    [Serializable]
    public class AlbumDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("title")]
        public string Title { get; set; }

        [BsonElement("artist")]
        public string Artist { get; set; }
    }

    [Serializable]
    public class SongDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("title")]
        public string Title { get; set; }

        [BsonElement("artist")]
        public string Artist { get; set; }

        [BsonElement("album")]
        public string Album { get; set; }

        [BsonElement("familyFriendly")]
        public bool FamilyFriendly { get; set; }
    }

    [Serializable]
    public class TracklistEntryDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("songId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SongId { get; set; }

        [BsonElement("title")]
        public string Title { get; set; }

        [BsonElement("artist")]
        public string Artist { get; set; }

        [BsonElement("album")]
        public string Album { get; set; }

        [BsonElement("duration")]
        public int Duration { get; set; } // in seconds

        [BsonElement("status")]
        public string Status { get; set; } // queued, playing, played, skipped

        [BsonElement("priority")]
        public int Priority { get; set; } // 1 = highest priority

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; }

        [BsonElement("playedAt")]
        public DateTime? PlayedAt { get; set; }

        [BsonElement("requestedBy")]
        public string RequestedBy { get; set; }

        [BsonElement("masterId")]
        public string MasterId { get; set; }

        [BsonElement("slaveId")]
        public string SlaveId { get; set; }

        [BsonElement("existsAtMaster")]
        public bool ExistsAtMaster { get; set; }

        [BsonElement("length")]
        public int? Length { get; set; } // Audio file length in seconds (set by master when existsAtMaster is true)
    }

    public static class TracklistStatus
    {
        public const string Queued = "queued";
        public const string Playing = "playing";
        public const string Played = "played";
        public const string Skipped = "skipped";
    }
}

