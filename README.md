NEventStore.Persistence.Mongo
===

Mongo Persistence Engine for NEventStore

NEventStore.Persistence.MongoDB currently supports:

- .net framework 4.6.2
- .net standard 2.0

Build Status
===

Branches:

- master [![Build status](https://ci.appveyor.com/api/projects/status/8euhhjl05lhng8ka/branch/master?svg=true)](https://ci.appveyor.com/project/AGiorgetti/neventstore-persistence-mongodb/branch/master)
- develop [![Build status](https://ci.appveyor.com/api/projects/status/8euhhjl05lhng8ka/branch/develop?svg=true)](https://ci.appveyor.com/project/AGiorgetti/neventstore-persistence-mongodb/branch/develop)


Information
===

ChangeLog can be found [here](Changelog.md)

## How to Build (locally)

- clone the repository with:

```
git clone --recursive https://github.com/NEventStore/NEventStore.Persistence.MongoDB.git
```

or

```
git clone https://github.com/NEventStore/NEventStore.Persistence.MongoDB.git
git submodule update
```

To build the project locally on a Windows Machine:

- Optional: update `.\src\.nuget\NEventStore.Persistence.MongoDB.nuspec` file if needed (before creating relase packages).
- Open a Powershell console in Administrative mode and run the build script `build.ps1` in the root of the repository.

## How to Run Unit Tests (locally)

- Install Database engines or use Docker to run them in a container (you can use the scripts in `./docker` folder).
- Define the following environment variables:

  ```
  NEventStore.MongoDB="mongodb://localhost:50002/NEventStore?replicaSet=rs0"
  ```

## Run Tests in Visual Studio

To run tests in visual studio using NUnit as a Test Runner you need to explicitly exclude "Explicit Tests" from running adding the following filter in the test explorer section:

```
-Trait:"Explicit"
```

## Configure / Customize Commit Serialization

You can configure the serialization process using the standard methods offered by the MongoDB C# driver.

You'll need to specify the class mapping or implement an IBsonSerializationProvider for the ```MongoCommit``` class and registerm them before you start using any database operation.

For detailed information on how to configure the serialization in MongoDB head to the official [Serialization documentation](http://mongodb.github.io/mongo-csharp-driver/2.2/reference/bson/serialization/) 

### BsonClassMap

```csharp
public static void MapMongoCommit()
{
  if (!BsonClassMap.IsClassMapRegistered(typeof(MongoCommit)))
  {
    BsonClassMap.RegisterClassMap<MongoCommit>(cm =>
    {
      cm.AutoMap();
      // change how the Headers collection is serialized
      cm.MapMember(c => c.Headers)
        .SetSerializer(
          new ImpliedImplementationInterfaceSerializer<IDictionary<string, object>, Dictionary<string, object>>()
            .WithImplementationSerializer(
              new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>(global::MongoDB.Bson.Serialization.Options.DictionaryRepresentation.Document)
            ));
      // your custom mapping goes here
    });
  }
}
```

### IBsonSerializationProvider

```csharp
class MongoCommitProvider : IBsonSerializationProvider
{
    public IBsonSerializer GetSerializer(Type type)
    {
        if (type == typeof(MongoCommit))
        {
            return new MongoCommitSerializer();
        }
        return null;
    }
}

class MongoCommitSerializer : SerializerBase<MongoCommit>
{
    public override MongoCommit Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        // read the BsonDocument manually and return an instance of the MongoCommit class
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, int value)
    {
        // write the BsonDocument manually serializing each property of the MongoCommit class
    }
}
```

you can then register the serialization provider using: [```BsonSerializer.RegisterSerializationProvider```](http://api.mongodb.com/csharp/2.2/html/M_MongoDB_Bson_Serialization_BsonSerializer_RegisterSerializationProvider.htm)

## Transactions

To use MongoDb transactions...

Todo: complete instructions... see tests, we need overloads for anything that accepts the session object, they can be used only if the client was passed from outside, otherwise we need a way to expose it.


## How to contribute

### Git-Flow

This repository uses GitFlow to develop, if you are not familiar with GitFlow you can look at the following link.

* [A Successful Git Branching Model](http://nvie.com/posts/a-successful-git-branching-model/)
* [Git Flow Cheat-Sheet](http://danielkummer.github.io/git-flow-cheatsheet/)
* [Git Flow for GitHub](https://datasift.github.io/gitflow/GitFlowForGitHub.html)

### Installing and configuring Git Flow

Probably the most straightforward way to install GitFlow on your machine is installing [Git Command Line](https://git-for-windows.github.io/), then install the [Visual Studio Plugin for Git-Flow](https://visualstudiogallery.msdn.microsoft.com/27f6d087-9b6f-46b0-b236-d72907b54683). This plugin is accessible from the **Team Explorer** menu and allows you to install GitFlow extension directly from Visual Studio with a simple click. The installer installs standard GitFlow extension both for command line and for Visual Studio Plugin.

Once installed you can use GitFlow right from Visual Studio or from Command line, which one you prefer.

### Build machine and GitVersion

Build machine uses [GitVersion](https://github.com/GitTools/GitVersion) to manage automatic versioning of assemblies and Nuget Packages. You need to be aware that there are a rule that does not allow you to directly commit on master, or the build will fail. 

A commit on master can be done only following the [Git-Flow](http://nvie.com/posts/a-successful-git-branching-model/) model, as a result of a new release coming from develop, or with an hotfix. 

### Quick Info for NEventstore projects

Just clone the repository and from command line checkout develop branch with 

```
git checkout develop
```

Then from command line run GitFlow initialization scripts

```
git flow init
```

You can leave all values as default. Now your repository is GitFlow enabled.

### Note on Nuget version on Nuspec

Remember to update `.\src\.nuget\NEventStore.Persistence.MongoDB.nuspec` file if needed (before creating relase packages).

The .nuspec file is needed because the new `dotnet pack` command has problems dealing with ProjectReferences, submodules get the wrong version number.

While we are on develop branch, (suppose we just bumped major number so the driver version number is 6.0.0-unstablexxxx), we need to declare that this persistence driver depends from a version greater than the latest published. If the latest version of NEventStore 5.x.x wave iw 5.4.0 we need to declare this package dependency as

(5.4, 7)

This means, that we need a NEventStore greater than the latest published, but lesser than the next main version. This allows version 6.0.0-unstable of NEventStore to satisfy the dependency. We remember that prerelease package are considered minor than the stable package. Es.

5.4.0
5.4.1
6.0.0-unstable00001
6.0.0