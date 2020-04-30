// Copyright (c) Improbable Worlds Ltd, All Rights Reserved

#pragma once

#include "CoreMinimal.h"
#include "Framework/Commands/Commands.h"
#include "SpatialGDKEditorToolbarStyle.h"

class FSpatialGDKEditorToolbarCommands : public TCommands<FSpatialGDKEditorToolbarCommands>
{
public:
	FSpatialGDKEditorToolbarCommands()
		: TCommands<FSpatialGDKEditorToolbarCommands>(
			TEXT("SpatialGDKEditorToolbar"),
			NSLOCTEXT("Contexts", "SpatialGDKEditorToolbar", "SpatialGDKEditorToolbar Plugin"), NAME_None,
			FSpatialGDKEditorToolbarStyle::GetStyleSetName())
	{
	}

	virtual void RegisterCommands() override;

public:
	TSharedPtr<FUICommandInfo> CreateSpatialGDKSchema;
	TSharedPtr<FUICommandInfo> CreateSpatialGDKSchemaFull;
	TSharedPtr<FUICommandInfo> DeleteSchemaDatabase;
	TSharedPtr<FUICommandInfo> CreateSpatialGDKSnapshot;
	TSharedPtr<FUICommandInfo> StartNoAutomaticConnection;
	TSharedPtr<FUICommandInfo> StartLocalSpatialDeployment;
	TSharedPtr<FUICommandInfo> StartCloudSpatialDeployment;
	TSharedPtr<FUICommandInfo> StopSpatialDeployment;
	TSharedPtr<FUICommandInfo> LaunchInspectorWebPageAction;
	
	TSharedPtr<FUICommandInfo> OpenSimulatedPlayerConfigurationWindowAction;
	TSharedPtr<FUICommandInfo> OpenLaunchConfigurationEditorAction;
	TSharedPtr<FUICommandInfo> QuickDeployAction;
	TSharedPtr<FUICommandInfo> EnableBuildClientWorker;
	TSharedPtr<FUICommandInfo> EnableBuildSimulatedPlayer;

	TSharedPtr<FUICommandInfo> StartSpatialService;
	TSharedPtr<FUICommandInfo> StopSpatialService;
	TSharedPtr<FUICommandInfo> EnableSpatialNetworking;
	TSharedPtr<FUICommandInfo> GDKEditorSettings;
	TSharedPtr<FUICommandInfo> NoAutomaticConnection;
	TSharedPtr<FUICommandInfo> LocalDeployment;
	TSharedPtr<FUICommandInfo> CloudDeployment;
};
