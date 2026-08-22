using Generated;
using ProtoBuf;

namespace ProceduralCubemapApi.Common.Networking
{
    /// <summary>
    /// Authorizes or cancels a recipe that clients may already be preparing.
    /// </summary>
    [NetworkPayload(3)]
    [ProtoContract]
    internal partial class RuntimeRevisionDecision
    {
        [ProtoMember(1)]
        public long PlanetEntityId;

        [ProtoMember(2)]
        public ulong Revision;

        [ProtoMember(3)]
        public bool Commit;

        [ProtoMember(4)]
        public string RuntimeSubtype;
    }
}
