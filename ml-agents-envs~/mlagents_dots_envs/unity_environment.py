import atexit
import numpy as np
import subprocess
from typing import List, Optional, Tuple

from mlagents_envs.side_channel.side_channel import SideChannel

from mlagents_envs.base_env import (
    BaseEnv,
    DecisionSteps,
    TerminalSteps,
    BehaviorSpec,
    BehaviorName,
)
from mlagents_envs.timers import timed
from mlagents_envs.exception import UnityCommunicationException, UnityActionException

from mlagents_dots_envs.shared_memory.shared_memory_communicator import (
    SharedMemoryCommunicator,
)

from mlagents_dots_envs.env_utils import (
    get_side_channels,
    executable_launcher,
    generate_side_channel_data,
    parse_side_channel_message,
    returncode_to_signal_name,
    validate_environment_path as ttt,
)

from mlagents_envs.logging_util import get_logger


logger = get_logger(__name__)


class UnityEnvironment(BaseEnv):
    API_VERSION = "API-14"  # TODO : REMOVE
    DEFAULT_EDITOR_PORT = 5004  # TODO : REMOVE
    BASE_ENVIRONMENT_PORT = 5005  # TODO : REMOVE
    PORT_COMMAND_LINE_ARG = "--mlagents-port"  # TODO : REMOVE

    @staticmethod
    def validate_environment_path(path__):
        return ttt(path__)

    def __init__(
        self,
        worker_id: Optional[int] = None,  # TODO : REMOVE
        seed: Optional[int] = None,  # TODO : REMOVE
        docker_trainin: Optional[bool] = None,  # TODO : REMOVE
        no_graphics: Optional[bool] = None,  # TODO : REMOVE
        base_port: Optional[int] = None,  # TODO : REMOVE
        file_name: Optional[str] = None,
        args: Optional[List[str]] = None,
        side_channels: Optional[List[SideChannel]] = None,
    ):
        """
        Starts a new unity environment and establishes a connection with it.

        :string file_name: Name of Unity environment binary. If None, will try to
        connect to the Editor.
        :list args: Addition Unity command line arguments
        :list side_channels: Additional side channel for not-rl communication with Unity
        """
        args = args or []
        atexit.register(self.close)
        executable_name = file_name

        if executable_name is None:
            assert args == []
        self._side_channels = get_side_channels(side_channels)
        self._communicator = SharedMemoryCommunicator(executable_name is None)

        # The process that is started. If None, no process was started
        self.proc1 = None
        if executable_name is not None:
            self.proc1 = executable_launcher(
                executable_name, self._communicator.communicator_id, args
            )
        else:
            logger.info(
                "Start training by pressing the Play button in the Unity Editor."
            )
        self._env_specs = self._communicator.generate_specs()
        self._communicator.give_unity_control()
        self._communicator.wait_for_unity()

    def reset(self) -> None:
        self._step(reset=True)

    @timed
    def step(self) -> None:
        self._step(reset=False)

    def _step(self, reset: bool = False) -> None:
        if not self._communicator.active:
            raise UnityCommunicationException("Communicator has stopped.")
        channel_data = generate_side_channel_data(self._side_channels)
        self._communicator.write_side_channel_data(channel_data)
        self._communicator.give_unity_control(reset)
        self._communicator.wait_for_unity()
        if not self._communicator.active:
            raise UnityCommunicationException("Communicator has stopped.")
        parse_side_channel_message(
            self._side_channels, self._communicator.read_and_clear_side_channel_data()
        )
        if len(self._env_specs) != self._communicator.num_behaviors:
            self._env_specs = self._communicator.generate_specs()

    def get_behavior_names(self) -> List[str]:
        return list(self._env_specs.keys())

    def _assert_behavior_exists(self, behavior_name: BehaviorName) -> None:
        if behavior_name not in self._env_specs:
            raise UnityActionException(
                f"The behavior {behavior_name} does not correspond to one existing "
                f"in the environment"
            )

    def set_actions(self, behavior_name: BehaviorName, action: np.array) -> None:
        self._assert_behavior_exists(behavior_name)
        expected_n_agents = self._communicator.get_n_decisions_requested(behavior_name)
        if expected_n_agents == 0 and len(action) != 0:
            raise UnityActionException(
                f"The behavior {behavior_name} does not need an input this step"
            )
        spec = self._env_specs[behavior_name]
        expected_type = np.float32 if spec.is_action_continuous() else np.int32
        expected_shape = (expected_n_agents, spec.action_size)
        if action.shape != expected_shape:
            raise UnityActionException(
                f"The behavior {behavior_name} needs an input of dimension"
                f"{expected_shape} but received input of dimension {action.shape}"
            )
        if action.dtype != expected_type:
            action = action.astype(expected_type)
        self._communicator.set_actions(behavior_name, action)

    def set_action_for_agent(
        self, behavior_name: BehaviorName, agent_id: int, action: np.array
    ) -> None:
        raise NotImplementedError("Method not implemented.")

    def get_steps(
        self, behavior_name: BehaviorName
    ) -> Tuple[DecisionSteps, TerminalSteps]:
        self._assert_behavior_exists(behavior_name)
        return self._communicator.get_steps(behavior_name)

    def get_behavior_spec(self, behavior_name: BehaviorName) -> BehaviorSpec:
        self._assert_behavior_exists(behavior_name)
        return self._env_specs[behavior_name]

    def close(self):
        """
        Sends a shutdown signal to the unity environment, and closes the communication.
        """
        self._communicator.close()
        if self.proc1 is not None:
            # Wait a bit for the process to shutdown, but kill it if it takes too long
            try:
                self.proc1.wait(timeout=5)
                signal_name = returncode_to_signal_name(self.proc1.returncode)
                signal_name = f" ({signal_name})" if signal_name else ""
                return_info = (
                    f"Environment shut down with return"
                    f"code{self.proc1.returncode}{signal_name}."
                )
                logger.info(return_info)
            except subprocess.TimeoutExpired:
                logger.info("Environment timed out shutting down. Killing...")
                self.proc1.kill()
            # Set to None so we don't try to close multiple times.
            self.proc1 = None
