NEventStore.Persistence.Mongo
=============================

Mongo Persistence Engine for NEventStore v6

Changelog at https://github.com/NEventStore/NEventStore.Persistence.MongoDB/blob/master/src/NEventStore.Persistence.MongoDB/Readme.txt

##How to contribute

###Git-Flow

This repository uses GitFlow to develop, if you are not familiar with GitFlow you can look at the following link.

* [A Successful Git Branching Model](http://nvie.com/posts/a-successful-git-branching-model/)
* [Git Flow Cheat-Sheet](http://danielkummer.github.io/git-flow-cheatsheet/)
* [Git Flow for GitHub](https://datasift.github.io/gitflow/GitFlowForGitHub.html)

###Installing and configuring Git Flow

Probably the most straightforward way to install GitFlow on your machine is installing [Git Command Line](https://git-for-windows.github.io/), then install the [Visual Studio Plugin for Git-Flow](https://visualstudiogallery.msdn.microsoft.com/27f6d087-9b6f-46b0-b236-d72907b54683). This plugin is accessible from the **Team Explorer** menu and allows you to install GitFlow extension directly from Visual Studio with a simple click. The installer installs standard GitFlow extension both for command line and for Visual Studio Plugin.

Once installed you can use GitFlow right from Visual Studio or from Command line, which one you prefer.

###Build machine and GitVersion

Build machine uses [GitVersion](https://github.com/GitTools/GitVersion) to manage automatic versioning of assemblies and Nuget Packages. You need to be aware that there are a rule that does not allow you to directly commit on master, or the build will fail. 

A commit on master can be done only following the [Git-Flow](http://nvie.com/posts/a-successful-git-branching-model/) model, as a result of a new release coming from develop, or with an hotfix. 

###Quick Info for NEventstore projects

Just clone the repository and from command line checkout develop branch with 

```
git checkout develop
```

Then from command line run GitFlow initialization scripts

```
git flow init
```

You can leave all values as default. Now your repository is GitFlow enabled.


