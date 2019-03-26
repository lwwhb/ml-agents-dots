﻿using System;
using ECS_MLAgents_v0.Core;
using ECS_MLAgents_v0.Core.Inference;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ECS_MLAgents_v0.Example.SpaceWars.Scripts
{

    public class SmartShipSystem : AgentSystem<ShipSensor, Steering> { }
    public class PlayerShipSystem : AgentSystem<ShipSensor, Steering> { }


    public class Manager : MonoBehaviour
    {
        public float TargetAngle;
        public GameObject target;
        public GameObject Camera;

        public enum DecisionSelector { NeuralNetwork, ExternalDecision };
        public DecisionSelector shipDecisionSelector;
        public DecisionSelector playerDecisionSelector;

        private EntityManager manager;
        public GameObject prefab;


        private SmartShipSystem _shipSystemA;
        private PlayerShipSystem _playerSystem;

        private SensorPopulate _sensorSystem;
        private ImpactSystem _impactSystem;

        public NNModel model;

        private Entity _playerEntity;

        void Start()
        {
            Time.captureFramerate = 60;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            manager = World.Active.GetOrCreateManager<EntityManager>();
            _sensorSystem = World.Active.GetOrCreateManager<SensorPopulate>();
            _impactSystem = World.Active.GetOrCreateManager<ImpactSystem>();
            _impactSystem.Radius = 20;

            _shipSystemA = World.Active.GetExistingManager<SmartShipSystem>();
            if (shipDecisionSelector == DecisionSelector.ExternalDecision)
            {
                _shipSystemA.Decision = new ExternalDecision<ShipSensor, Steering>();
            }
            else
            {
                _shipSystemA.Decision = new NNDecision<ShipSensor, Steering>(model);
            }
            _playerSystem = World.Active.GetExistingManager<PlayerShipSystem>();
            if (playerDecisionSelector == DecisionSelector.ExternalDecision)
            {
                _playerSystem.Decision = new HumanDecision<ShipSensor>();
            }
            else
            {
                _playerSystem.Decision = new NNDecision<ShipSensor, Steering>(model);
            }
            _playerSystem.SetNewComponentGroup(typeof(PlayerFlag));
            _shipSystemA.DecisionRequester = new FixedTimeRequester(0.1f);

            _playerEntity = manager.Instantiate(prefab);
            MakeSpaceShip(_playerEntity);
            manager.AddComponentData(_playerEntity, new PlayerFlag());
            manager.SetComponentData(_playerEntity, new Ship
            {
                Fire = 0,
                ReloadTime = 1f,
                MaxReloadTime = 1f
            });


            Spawn(10);

            //            Debug.Log(typeof(ShipSensor).GetCustomAttributes(typeof(SerializableAttribute), true)[0]);
            AttributeUtility.GetSensorMetaData(typeof(ShipSensor));
        }


        void FixedUpdate()
        {
            // World.Active.GetOrCreateManager<SimulationSystemGroup>();
        }

        void Update()
        {



            // for (var i = 0; i < 10; i++){
            //     foreach(var behavior in World.Active.BehaviourManagers)
            //     {
            //      behavior.Update();
            //     }
            // }
            //            Debug.Log(Application.targetFrameRate);
            float3 targetPos = 100 * new float3(math.cos(TargetAngle), 0, math.sin(TargetAngle));
            _sensorSystem.Center = targetPos;
            _impactSystem.Center = targetPos;
            target.transform.position = targetPos;

            TargetAngle += Time.deltaTime / 20f;
            if (Input.GetKeyDown(KeyCode.A))
            {
                Spawn(1);
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                Spawn(100);
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                Spawn(1000);
            }

            var camPosition = manager.GetComponentData<Position>(_playerEntity).Value;
            var camRotation = manager.GetComponentData<Rotation>(_playerEntity).Value;
            camPosition += math.mul(camRotation, new float3(0, 0, 5));
            Camera.transform.position = Vector3.Lerp(Camera.transform.position, camPosition, 0.1f);
            Camera.transform.rotation = Quaternion.Lerp(Camera.transform.rotation, camRotation, 0.1f);

        }

        void Spawn(int amount)
        {
            Globals.NumberShips += amount;
            NativeArray<Entity> entities = new NativeArray<Entity>(amount, Allocator.Temp);
            manager.Instantiate(prefab, entities);
            for (int i = 0; i < amount; i++)
            {
                MakeSpaceShip(entities[i]);

            }
            entities.Dispose();
        }

        private void MakeSpaceShip(Entity ent)
        {
            float valX = Random.Range(-1f, 1f);
            float valY = Random.Range(-1f, 1f);
            float valZ = Random.Range(-1f, 1f);
            float valD = Random.Range(0f, 1f);



            float3 SpawnOffset = valD *
                                 Globals.SPAWN_DISTANCE *
                                 math.normalize(new float3(valX, valY, valZ));

            manager.SetComponentData(ent, new Steering());
            manager.SetComponentData(ent,
                new Position
                {
                    Value = SpawnOffset
                });
            manager.SetComponentData(ent,
                new Rotation
                {
                    Value = quaternion.EulerXYZ(
                        math.normalize(new float3(
                            Random.Range(-1f, 1f),
                            Random.Range(-1f, 1f),
                            Random.Range(-1f, 1f)))
                    )
                });
            manager.SetComponentData(ent,
                new Scale
                {
                    Value = new float3(
                        Globals.SHIP_SCALE_X,
                        Globals.SHIP_SCALE_Y,
                        Globals.SHIP_SCALE_Z)
                });
            manager.SetComponentData(ent, new Ship
            {
                Fire = 0,
                ReloadTime = Random.Range(0f, Globals.RELOAD_TIME),
                MaxReloadTime = Globals.RELOAD_TIME,
                TargetOffset = -SpawnOffset
            });
            manager.SetComponentData(ent, new ShipSensor());
        }

    }


}

