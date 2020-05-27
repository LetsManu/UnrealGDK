// Copyright (c) Improbable Worlds Ltd, All Rights Reserved

#pragma once

#include "LoadBalancing/LayeredLBStrategy.h"

#include "CoreMinimal.h"
#include "GameFramework/WorldSettings.h"

#include "SpatialWorldSettings.generated.h"

class UAbstractLBStrategy;
class UAbstractLockingPolicy;

UCLASS()
class SPATIALGDK_API ASpatialWorldSettings : public AWorldSettings
{
	GENERATED_BODY()

public:
	UPROPERTY(EditAnywhere, Config, Category = "Load Balancing")
	TSubclassOf<UAbstractLBStrategy> LoadBalanceStrategy;

	UPROPERTY(EditAnywhere, Config, Category = "Load Balancing")
	TSubclassOf<UAbstractLockingPolicy> LockingPolicy;

	/** Layer load balancing  configuration. */
	UPROPERTY(EditAnywhere, Config, Category = "Multi Worker")
	TMap<FName, FLBLayerInfo> WorkerLBLayers;
};
