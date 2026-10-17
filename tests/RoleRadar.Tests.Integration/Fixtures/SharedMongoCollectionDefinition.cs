using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Fixtures;

[CollectionDefinition(Name)]
public sealed class SharedMongoCollectionDefinition : ICollectionFixture<SharedMongoFixture>
{
    public const string Name = "SharedMongoCollection";
}
