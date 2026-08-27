import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, call, patch


REPO_ROOT = Path(__file__).resolve().parents[1]
DRIVER_PATH = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
SPEC = importlib.util.spec_from_file_location("run_api36_editing_e2e", DRIVER_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"could not load {DRIVER_PATH}")
DRIVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = DRIVER
SPEC.loader.exec_module(DRIVER)


class FakeDevice(DRIVER.Device):
    def __init__(
        self,
        evidence: Path,
        hierarchy_output: str,
        dump_output: str = "UI hierarchy dumped",
    ) -> None:
        super().__init__(Path("/unused/adb"), "emulator-5554", evidence)
        self.hierarchy_output = hierarchy_output
        self.dump_output = dump_output

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        return self.dump_output

    def run(
        self,
        *arguments: str,
        timeout: int = 120,
        text: bool = True,
        check: bool = True,
    ) -> subprocess.CompletedProcess:
        return subprocess.CompletedProcess(
            args=list(arguments),
            returncode=0,
            stdout=self.hierarchy_output,
            stderr="",
        )


class RecordingDevice(DRIVER.Device):
    def __init__(
        self,
        evidence: Path,
        display_size: str,
        locale_output: str = "en-US",
    ) -> None:
        super().__init__(Path("/unused/adb"), "emulator-5554", evidence)
        self.display_size_output = display_size
        self.locale_output = locale_output
        self.commands: list[tuple[str, ...]] = []
        self.nodes: list[DRIVER.UiNode] = []
        self.input_method_output = ""
        self.hide_keyboard_on_nav_tap = False
        self.hide_keyboard_on_escape = False

    def shell(self, *arguments: str, timeout: int = 120) -> str:
        self.commands.append(arguments)
        if arguments == ("wm", "size"):
            return self.display_size_output
        if arguments == ("getprop", "persist.sys.locale"):
            return self.locale_output
        if arguments == ("getprop", "ro.product.locale"):
            return "en-US"
        if arguments == ("dumpsys", "input_method"):
            return self.input_method_output
        if (
            self.hide_keyboard_on_escape
            and arguments == ("input", "keyevent", "111")
        ):
            self.input_method_output = "mImeWindowVis=0\n      mInputShown=false"
        elif (
            self.hide_keyboard_on_nav_tap
            and arguments == ("input", "tap", "162", "2350")
        ):
            self.input_method_output = "mImeWindowVis=0\n      mInputShown=false"
        elif arguments[:2] == ("input", "tap") and self.nodes:
            tap_x, tap_y = (int(value) for value in arguments[2:4])
            native_tabs = [
                node
                for node in self.nodes
                if node.attributes.get("class") == "android.widget.FrameLayout"
                and any(
                    node.attributes.get("content-desc") in labels
                    for labels in DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE.values()
                )
            ]
            tapped_tab = next(
                (
                    node
                    for node in native_tabs
                    if node.bounds[0] <= tap_x < node.bounds[2]
                    and node.bounds[1] <= tap_y < node.bounds[3]
                ),
                None,
            )
            if tapped_tab is not None:
                for node in native_tabs:
                    selected = node is tapped_tab
                    node.attributes["selected"] = str(selected).lower()
                    node.attributes["clickable"] = str(not selected).lower()
            else:
                for node in self.nodes:
                    node.attributes["focused"] = "true"
        elif arguments[:2] == ("input", "text"):
            value = arguments[2].replace("%s", " ")
            for node in self.nodes:
                if node.attributes.get("focused") == "true":
                    node.attributes["text"] = value
        elif arguments == ("input", "keyevent", "67"):
            for node in self.nodes:
                if node.attributes.get("focused") == "true":
                    node.attributes["text"] = ""
        return ""

    def hierarchy(self) -> list[DRIVER.UiNode]:
        return self.nodes


class Api36EditingE2EDriverTests(unittest.TestCase):
    @staticmethod
    def successful_am_start(component: str) -> subprocess.CompletedProcess:
        return subprocess.CompletedProcess(
            args=["adb", "shell", "am", "start"],
            returncode=0,
            stdout=(
                "Starting: Intent { act=android.intent.action.MAIN "
                f"cat=[android.intent.category.LAUNCHER] cmp={component} }}\n"
                "Status: ok\n"
                "LaunchState: COLD\n"
                f"Activity: {component}\n"
                "Complete\n"
            ),
            stderr="",
        )

    @staticmethod
    def native_phone_tabs(
        *,
        selected_label: str = "Runner",
        selected_index: int | None = None,
        labels: tuple[str, ...] = DRIVER.PHONE_SHELL_DESTINATION_LABELS,
        widths: tuple[tuple[int, int], ...] = (
            (0, 270),
            (270, 540),
            (540, 810),
            (810, 1080),
        ),
    ) -> list[DRIVER.UiNode]:
        return [
            DRIVER.UiNode(
                {
                    "resource-id": "",
                    "package": DRIVER.PACKAGE,
                    "class": "android.widget.FrameLayout",
                    "content-desc": label,
                    "enabled": "true",
                    "focusable": "true",
                    "selected": str(
                        index == selected_index
                        if selected_index is not None
                        else label == selected_label
                    ).lower(),
                    "clickable": str(
                        index != selected_index
                        if selected_index is not None
                        else label != selected_label
                    ).lower(),
                    "bounds": f"[{left},2190][{right},2337]",
                }
            )
            for index, (label, (left, right)) in enumerate(
                zip(labels, widths, strict=True)
            )
        ]

    @staticmethod
    def phone_runner_page() -> DRIVER.UiNode:
        return DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/phone-runner-page",
                "package": DRIVER.PACKAGE,
                "class": "android.view.ViewGroup",
                "enabled": "true",
                "bounds": "[0,275][1080,2190]",
            }
        )

    @staticmethod
    def phone_runner_toolbar() -> DRIVER.UiNode:
        return DRIVER.UiNode(
            {
                "resource-id": "",
                "package": DRIVER.PACKAGE,
                "class": "android.widget.Button",
                "content-desc": "build-save-runner",
                "enabled": "true",
                "clickable": "true",
                "focusable": "true",
                "bounds": "[954,138][1080,264]",
            }
        )

    @staticmethod
    def phone_runner_route(
        route_id: str = "phone-runner-sheet",
        *,
        bounds: str = "[53,323][1028,362]",
    ) -> DRIVER.UiNode:
        return DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/{route_id}",
                "package": DRIVER.PACKAGE,
                "class": "android.widget.TextView",
                "enabled": "true",
                "clickable": "false",
                "focusable": "false",
                "text": (
                    "CREATION RUNNER"
                    if route_id == "phone-runner-create"
                    else "CAREER RUNNER"
                ),
                "bounds": bounds,
            }
        )

    @staticmethod
    def native_navigate_up(bounds: str) -> DRIVER.UiNode:
        return DRIVER.UiNode(
            {
                "resource-id": "",
                "package": DRIVER.PACKAGE,
                "class": "android.widget.ImageButton",
                "content-desc": "Navigate up",
                "enabled": "true",
                "clickable": "true",
                "focusable": "true",
                "bounds": bounds,
            }
        )

    def test_phone_shell_observation_requires_exact_live_destinations(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.display_size.return_value = (1080, 2400)
            nodes = self.native_phone_tabs()
            device.hierarchy.return_value = nodes

            observed = DRIVER.assert_phone_shell_surface(
                device,
                route_resource_id="phone-runner-create",
                evidence_prefix="creation-shell",
            )

            self.assertEqual(
                sorted(DRIVER.PHONE_SHELL_DESTINATION_IDS),
                observed["destinationResourceIds"],
            )
            self.assertEqual(
                sorted(DRIVER.PHONE_SHELL_DESTINATION_LABELS),
                observed["destinationLabels"],
            )
            self.assertEqual(
                {
                    destination_id: [label]
                    for destination_id, label in DRIVER.PHONE_SHELL_DESTINATION_MAPPING.items()
                },
                observed["destinationMapping"],
            )
            self.assertEqual([], observed["forbiddenDestinationLabels"])
            self.assertEqual([], observed["forbiddenSupportLabels"])
            self.assertTrue(
                (Path(temporary) / "creation-shell-observation.json").is_file()
            )
            device.capture.assert_not_called()

    def test_phone_runner_route_rejects_duplicate_and_wrong_lifecycle_roots(self) -> None:
        for name, created, route_ids, expected_capture in (
            (
                "mixed",
                False,
                ("phone-runner-create", "phone-runner-sheet"),
                "phone-runner-route-cardinality-invalid",
            ),
            (
                "duplicate",
                True,
                ("phone-runner-sheet", "phone-runner-sheet"),
                "phone-runner-route-cardinality-invalid",
            ),
            (
                "wrong-lifecycle",
                True,
                ("phone-runner-create",),
                "phone-runner-route-lifecycle-mismatch",
            ),
        ):
            with self.subTest(name=name):
                device = Mock(spec=DRIVER.Device)
                device.hierarchy.return_value = [
                    self.phone_runner_route(route_id)
                    for route_id in route_ids
                ]
                with self.assertRaises(RuntimeError):
                    DRIVER.wait_for_phone_runner_route(
                        device,
                        created=created,
                        timeout=1,
                    )
                device.capture.assert_called_once_with(expected_capture)

    def test_phone_runner_route_resets_deep_root_before_binding_lifecycle(self) -> None:
        device = Mock(spec=DRIVER.Device)
        page = self.phone_runner_page()
        toolbar = self.phone_runner_toolbar()
        clipped_route = self.phone_runner_route(bounds="[53,-6506][1028,-6467]")
        route = self.phone_runner_route()
        device.hierarchy.side_effect = [
            [page, toolbar, clipped_route],
            [page, toolbar, route],
        ]
        device.node_has_tappable_bounds.side_effect = (
            lambda node: node is not clipped_route
        )

        with patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll:
            observed = DRIVER.wait_for_phone_runner_route(device, created=True)

        self.assertIs(route, observed)
        reset_scroll.assert_called_once_with(device, swipes=48)
        device.shell.assert_not_called()

    def test_phone_runner_route_rejects_hidden_duplicate_canonical_marker(self) -> None:
        device = Mock(spec=DRIVER.Device)
        visible = self.phone_runner_route()
        hidden = self.phone_runner_route()
        hidden.attributes["visible-to-user"] = "false"
        device.hierarchy.return_value = [visible, hidden]

        with self.assertRaisesRegex(RuntimeError, "both creation and career roots"):
            DRIVER.wait_for_phone_runner_route(device, created=True, timeout=1)

        device.capture.assert_called_once_with(
            "phone-runner-route-cardinality-invalid"
        )

    def test_phone_runner_route_rejects_interactive_lifecycle_marker(self) -> None:
        device = Mock(spec=DRIVER.Device)
        route = self.phone_runner_route()
        route.attributes["clickable"] = "true"
        root = [self.phone_runner_page(), self.phone_runner_toolbar(), route]
        device.hierarchy.side_effect = [root, root]
        device.node_has_tappable_bounds.return_value = True

        with (
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "pinned noninteractive native role"),
        ):
            DRIVER.return_to_phone_runner_root(device, created=True)

        reset_scroll.assert_not_called()
        device.capture.assert_called_once_with(
            "phone-runner-route-structure-invalid"
        )

    def test_phone_runner_root_unwinds_nested_pages_before_resetting_viewport(self) -> None:
        device = Mock(spec=DRIVER.Device)
        first_up = self.native_navigate_up("[20,100][120,200]")
        second_up = self.native_navigate_up("[20,200][120,300]")
        runner_route = self.phone_runner_route()
        runner_toolbar = self.phone_runner_toolbar()
        runner_page = self.phone_runner_page()
        root_nodes = [runner_page, runner_route, runner_toolbar]
        device.hierarchy.side_effect = [
            [
                DRIVER.UiNode({"resource-id": "collection-editor-gear-item"}),
                first_up,
            ],
            [DRIVER.UiNode({"resource-id": "build-section-gear"}), second_up],
            root_nodes,
            root_nodes,
        ]
        device.node_has_tappable_bounds.return_value = True

        with (
            patch.object(DRIVER.time, "sleep"),
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
        ):
            observed = DRIVER.return_to_phone_runner_root(device, created=True)

        self.assertIs(runner_route, observed)
        self.assertEqual(
            [
                call("input", "tap", "70", "150"),
                call("input", "tap", "70", "250"),
            ],
            device.shell.call_args_list,
        )
        reset_scroll.assert_not_called()

    def test_phone_runner_root_does_not_accept_toolbar_without_exact_route(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.hierarchy.return_value = [self.phone_runner_toolbar()]

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=[0.0, 0.0, 2.0]),
            patch.object(DRIVER.time, "sleep"),
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        device.shell.assert_not_called()
        reset_scroll.assert_not_called()
        device.capture.assert_called_once_with("phone-runner-root-unavailable")

    def test_phone_runner_root_does_not_reset_for_hidden_root_toolbar(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.hierarchy.return_value = [
            self.phone_runner_page(),
            self.phone_runner_toolbar(),
        ]
        device.node_has_tappable_bounds.side_effect = [True, False]

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=[0.0, 0.0, 2.0]),
            patch.object(DRIVER.time, "sleep"),
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        reset_scroll.assert_not_called()
        device.shell.assert_not_called()

    def test_phone_runner_root_accepts_raw_api36_dump_without_visibility_extension(self) -> None:
        device = Mock(spec=DRIVER.Device)
        page = self.phone_runner_page()
        toolbar = self.phone_runner_toolbar()
        route = self.phone_runner_route()
        root = [page, toolbar, route]
        device.hierarchy.side_effect = [root, root]
        device.node_has_tappable_bounds.return_value = True

        with (
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
        ):
            observed = DRIVER.return_to_phone_runner_root(device, timeout=1)

        self.assertIs(route, observed)
        reset_scroll.assert_not_called()
        device.shell.assert_not_called()

    def test_phone_runner_root_rejects_toolbar_outside_viewport(self) -> None:
        device = Mock(spec=DRIVER.Device)
        toolbar = self.phone_runner_toolbar()
        toolbar.attributes["bounds"] = "[954,2500][1080,2600]"
        device.hierarchy.return_value = [
            self.phone_runner_page(),
            toolbar,
            self.phone_runner_route(),
        ]
        device.node_has_tappable_bounds.side_effect = lambda node: node is not toolbar

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=[0.0, 0.0, 2.0]),
            patch.object(DRIVER.time, "sleep"),
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        reset_scroll.assert_not_called()
        device.shell.assert_not_called()

    def test_phone_runner_root_rejects_route_omitted_after_viewport_reset(self) -> None:
        device = Mock(spec=DRIVER.Device)
        route = self.phone_runner_route(bounds="[53,-6506][1028,-6467]")
        root = [self.phone_runner_page(), self.phone_runner_toolbar(), route]
        device.hierarchy.side_effect = [root, root[:2], root[:2]]
        device.node_has_tappable_bounds.side_effect = lambda node: node is not route

        with (
            patch.object(
                DRIVER.time,
                "monotonic",
                side_effect=[0.0, 0.0, 0.0, 2.0],
            ),
            patch.object(DRIVER.time, "sleep"),
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        reset_scroll.assert_called_once_with(device, swipes=48)
        device.shell.assert_not_called()

    def test_phone_runner_root_rejects_navigate_up_outside_viewport(self) -> None:
        device = Mock(spec=DRIVER.Device)
        navigate_up = self.native_navigate_up("[20,2500][120,2600]")
        device.hierarchy.return_value = [navigate_up]
        device.node_has_tappable_bounds.return_value = False

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=[0.0, 0.0, 2.0]),
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        device.shell.assert_not_called()

    def test_phone_runner_root_rejects_foreign_suffix_markers(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.hierarchy.return_value = [
            DRIVER.UiNode(
                {
                    "resource-id": "evil.package:id/phone-runner-page",
                    "package": "evil.package",
                    "class": "android.view.ViewGroup",
                    "enabled": "true",
                }
            ),
            self.phone_runner_toolbar(),
            DRIVER.UiNode(
                {
                    "resource-id": "evil.package:id/phone-runner-sheet",
                    "package": "evil.package",
                    "class": "android.widget.TextView",
                    "enabled": "true",
                    "text": "CAREER RUNNER",
                    "bounds": "[53,323][1028,362]",
                }
            ),
        ]

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=[0.0, 0.0, 2.0]),
            patch.object(DRIVER.time, "sleep"),
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        reset_scroll.assert_not_called()
        device.shell.assert_not_called()

    def test_phone_runner_root_rejects_route_still_offscreen_after_reset(self) -> None:
        device = Mock(spec=DRIVER.Device)
        clipped_route = self.phone_runner_route(bounds="[53,-6506][1028,-6467]")
        root = [self.phone_runner_page(), self.phone_runner_toolbar(), clipped_route]
        device.hierarchy.side_effect = [root, root]
        device.node_has_tappable_bounds.side_effect = lambda node: (
            node is not clipped_route
        )

        with (
            patch.object(DRIVER, "reset_scroll_to_top") as reset_scroll,
            self.assertRaisesRegex(RuntimeError, "not visible"),
        ):
            DRIVER.return_to_phone_runner_root(device, created=True)

        reset_scroll.assert_called_once_with(device, swipes=48)
        device.capture.assert_called_once_with("phone-runner-route-structure-invalid")

    def test_phone_runner_root_does_not_activate_disabled_navigate_up(self) -> None:
        device = Mock(spec=DRIVER.Device)
        disabled = self.native_navigate_up("[20,100][120,200]")
        disabled.attributes["enabled"] = "false"
        device.hierarchy.return_value = [disabled]

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=[0.0, 0.0, 2.0]),
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "Timed out proving the exact"),
        ):
            DRIVER.return_to_phone_runner_root(device, timeout=1)

        device.shell.assert_not_called()

    def test_phone_runner_root_unwind_is_bounded_and_fails_closed(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.hierarchy.return_value = [
            self.native_navigate_up("[20,100][120,200]")
        ]
        device.node_has_tappable_bounds.return_value = True

        with (
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "1 exact Navigate up activation"),
        ):
            DRIVER.return_to_phone_runner_root(device, max_back_steps=1)

        device.shell.assert_called_once_with("input", "tap", "70", "150")
        device.capture.assert_called_once_with("phone-runner-root-unwind-exhausted")

    def test_open_build_binds_phone_navigation_to_one_final_runner_root(self) -> None:
        device = Mock(spec=DRIVER.Device)
        destinations = tuple(
            zip(
                DRIVER.PHONE_SHELL_DESTINATION_IDS,
                self.native_phone_tabs(selected_label="Runners"),
                strict=True,
            )
        )
        with (
            patch.object(
                DRIVER,
                "wait_for_phone_shell_destinations",
                return_value=destinations,
            ) as wait_destinations,
            patch.object(
                DRIVER,
                "bind_phone_shell_destinations",
                return_value=destinations,
            ),
            patch.object(DRIVER, "return_to_phone_runner_root") as return_to_root,
        ):
            DRIVER.open_build(device, "phone")

        self.assertEqual(
            [
                call(
                    device,
                    timeout=45,
                    evidence_prefix="phone-destination-runner-tap-bind",
                ),
                call(
                    device,
                    timeout=45,
                    evidence_prefix="phone-destination-runner-tap-select",
                    selected_label="Runner",
                ),
            ],
            wait_destinations.call_args_list,
        )
        device.shell.assert_called_once_with("input", "tap", "405", "2263")
        device.tap.assert_not_called()
        return_to_root.assert_called_once_with(device)
        device.open_navigation_drawer.assert_not_called()

    def test_phone_destination_tap_uses_structural_native_tab_not_text_alias(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                *self.native_phone_tabs(selected_label="Runners"),
                DRIVER.UiNode(
                    {
                        "resource-id": "open-workspace-runner",
                        "package": DRIVER.PACKAGE,
                        "class": "android.widget.Button",
                        "text": "Runner",
                        "content-desc": "Runner",
                        "clickable": "true",
                        "bounds": "[80,400][800,520]",
                    }
                ),
            ]

            bound = DRIVER.bind_phone_shell_destinations(device)
            self.assertEqual(
                DRIVER.PHONE_SHELL_DESTINATION_IDS,
                tuple(resource_id for resource_id, _ in bound),
            )
            self.assertTrue(
                all(node.attributes.get("resource-id") == "" for _, node in bound),
                "Pinned MAUI/API-36 tabs must be mapped from their strict native structure.",
            )

            DRIVER.tap_phone_destination(
                device,
                "phone-destination-runner",
            )

            self.assertIn(("input", "tap", "405", "2263"), device.commands)
            self.assertNotIn(("input", "tap", "440", "460"), device.commands)
            with self.assertRaisesRegex(ValueError, "Unknown phone shell destination"):
                DRIVER.tap_phone_destination(device, "phone-destination-play")

    def test_structural_phone_destination_binding_accepts_exact_de_en_es_tuples(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            for language, labels in DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE.items():
                with self.subTest(language=language):
                    nodes = self.native_phone_tabs(
                        labels=labels,
                        selected_index=1,
                    )
                    bound = DRIVER.bind_phone_shell_destinations(device, nodes)
                    self.assertEqual(
                        DRIVER.PHONE_SHELL_DESTINATION_IDS,
                        tuple(resource_id for resource_id, _ in bound),
                    )

    def test_localized_label_contract_accepts_exact_german_and_spanish(self) -> None:
        for locale_tag, language in (("de-AT", "de"), ("es-MX", "es")):
            with self.subTest(locale_tag=locale_tag):
                labels = DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE[language]
                resolved = DRIVER.resolve_localized_ui_labels(
                    contract_id="phone-shell-destinations",
                    locale_tag=locale_tag,
                    observed_labels=labels,
                    labels_by_language=DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE,
                )
                self.assertEqual(language, resolved["language"])
                self.assertEqual(list(labels), resolved["observedLabels"])
                self.assertEqual([language], resolved["matchingLanguages"])

    def test_localized_label_contract_rejects_mixed_locale_tuple(self) -> None:
        german = DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE["de"]
        spanish = DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE["es"]
        mixed = (german[0], spanish[1], german[2], spanish[3])
        with self.assertRaisesRegex(RuntimeError, "expected exactly"):
            DRIVER.resolve_localized_ui_labels(
                contract_id="phone-shell-destinations",
                locale_tag="de-AT",
                observed_labels=mixed,
                labels_by_language=DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE,
            )

    def test_localized_label_contract_rejects_unknown_phone_locale(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "outside the exact DE/EN/ES"):
            DRIVER.resolve_localized_ui_labels(
                contract_id="phone-shell-destinations",
                locale_tag="fr-FR",
                observed_labels=DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE["en"],
                labels_by_language=DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE,
            )

    def test_localized_label_contract_rejects_ambiguous_language_tuple(self) -> None:
        ambiguous = {
            "en": ("Same", "Tuple"),
            "de": ("Same", "Tuple"),
            "es": ("Distinto", "Tuple"),
        }
        with self.assertRaisesRegex(RuntimeError, "unambiguous"):
            DRIVER.resolve_localized_ui_labels(
                contract_id="ambiguous-test",
                locale_tag="de-DE",
                observed_labels=ambiguous["de"],
                labels_by_language=ambiguous,
            )

    def test_phone_locale_evidence_records_exact_locale_language_and_native_labels(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = RecordingDevice(
                evidence,
                "Physical size: 1080x2400",
                locale_output="de-AT",
            )
            labels = DRIVER.PHONE_SHELL_DESTINATION_LABELS_BY_LANGUAGE["de"]
            device.nodes = self.native_phone_tabs(labels=labels, selected_index=0)

            receipt = DRIVER.record_phone_ui_locale_evidence(
                device,
                evidence_prefix="localized-proof",
                timeout=2,
            )

            self.assertEqual("pass", receipt["status"])
            self.assertEqual("de-AT", receipt["localeTag"])
            self.assertEqual("de", receipt["language"])
            self.assertEqual(list(labels), receipt["observedLabels"])
            self.assertEqual("persist.sys.locale", receipt["authorityProperty"])
            persisted = (evidence / "localized-proof-phone-ui-locale.json").read_text(
                encoding="utf-8"
            )
            self.assertEqual(receipt, json.loads(persisted))

    def test_structural_phone_destination_binding_fails_closed_on_adversarial_nodes(self) -> None:
        def altered(
            index: int,
            **attributes: str,
        ) -> list[DRIVER.UiNode]:
            nodes = self.native_phone_tabs()
            nodes[index] = DRIVER.UiNode({**nodes[index].attributes, **attributes})
            return nodes

        duplicate_selected = self.native_phone_tabs()
        duplicate_selected[0] = DRIVER.UiNode(
            {
                **duplicate_selected[0].attributes,
                "selected": "true",
                "clickable": "false",
            }
        )
        adversarial = {
            "missing": self.native_phone_tabs()[:3],
            "wrong-class": altered(0, **{"class": "android.widget.Button"}),
            "wrong-order": self.native_phone_tabs(
                labels=("Runner", "Runners", "Stories", "More")
            ),
            "wrong-geometry": altered(3, bounds="[800,2190][1080,2337]"),
            "one-pixel-gap": altered(1, bounds="[271,2190][540,2337]"),
            "disabled": altered(0, enabled="false"),
            "not-focusable": altered(0, focusable="false"),
            "bad-clickability": altered(1, clickable="true"),
            "duplicate-selected": duplicate_selected,
            "mismatched-resource": altered(
                0,
                **{"resource-id": "phone-destination-more"},
            ),
            "matching-but-nonempty-resource": altered(
                0,
                **{"resource-id": "phone-destination-runners"},
            ),
            "fifth-recognized": [
                *self.native_phone_tabs(),
                DRIVER.UiNode(
                    {
                        **self.native_phone_tabs()[0].attributes,
                        "bounds": "[0,2190][270,2337]",
                    }
                ),
            ],
            "fifth-unknown": [
                *self.native_phone_tabs(),
                DRIVER.UiNode(
                    {
                        **self.native_phone_tabs()[0].attributes,
                        "content-desc": "Settings",
                        "bounds": "[0,2190][270,2337]",
                    }
                ),
            ],
        }

        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            for name, nodes in adversarial.items():
                with self.subTest(name=name):
                    with self.assertRaises(RuntimeError):
                        DRIVER.bind_phone_shell_destinations(device, nodes)

            decoys = [
                *self.native_phone_tabs(),
                DRIVER.UiNode(
                    {
                        "resource-id": "body-runner",
                        "package": DRIVER.PACKAGE,
                        "class": "android.widget.Button",
                        "content-desc": "Runner",
                        "bounds": "[80,400][800,520]",
                    }
                ),
                DRIVER.UiNode(
                    {
                        "resource-id": "",
                        "package": DRIVER.PACKAGE,
                        "class": "android.widget.FrameLayout",
                        "content-desc": "Runners",
                        "bounds": "[0,300][1080,700]",
                    }
                ),
            ]
            bound = DRIVER.bind_phone_shell_destinations(device, decoys)
            self.assertEqual(DRIVER.PHONE_SHELL_DESTINATION_IDS, tuple(
                resource_id for resource_id, _ in bound
            ))

    def test_phone_destination_wait_requires_two_stable_selected_snapshots(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.display_size.return_value = (1080, 2400)
        device.dismiss_system_ui_anr.return_value = False
        device.hierarchy.side_effect = [
            self.native_phone_tabs(selected_label="Runners"),
            self.native_phone_tabs(selected_label="Runner"),
            self.native_phone_tabs(selected_label="Runner"),
        ]

        with patch.object(DRIVER.time, "sleep"):
            _, destinations = DRIVER.wait_for_phone_shell_destination_snapshot(
                device,
                timeout=5,
                evidence_prefix="stable-transition",
                selected_label="Runner",
            )

        self.assertEqual(3, device.hierarchy.call_count)
        self.assertEqual(
            ["Runner"],
            [
                DRIVER.PHONE_SHELL_DESTINATION_MAPPING[resource_id]
                for resource_id, node in destinations
                if node.attributes.get("selected") == "true"
            ],
        )
        device.capture.assert_not_called()

    def test_phone_destination_tap_refuses_changed_pre_tap_snapshot(self) -> None:
        device = Mock(spec=DRIVER.Device)
        stable_destinations = tuple(
            zip(
                DRIVER.PHONE_SHELL_DESTINATION_IDS,
                self.native_phone_tabs(selected_label="Runners"),
                strict=True,
            )
        )
        changed_destinations = tuple(
            zip(
                DRIVER.PHONE_SHELL_DESTINATION_IDS,
                self.native_phone_tabs(selected_label="More"),
                strict=True,
            )
        )
        with (
            patch.object(
                DRIVER,
                "wait_for_phone_shell_destinations",
                return_value=stable_destinations,
            ),
            patch.object(
                DRIVER,
                "bind_phone_shell_destinations",
                return_value=changed_destinations,
            ),
        ):
            with self.assertRaisesRegex(RuntimeError, "stale coordinates"):
                DRIVER.tap_phone_destination(device, "phone-destination-runner")

        device.capture.assert_called_once_with("phone-destination-runner-tap-stale")
        device.shell.assert_not_called()

    def test_phone_shell_observation_rejects_mapping_extras_and_tablet_roots(self) -> None:
        valid_nodes = self.native_phone_tabs()
        adversarial_nodes = {
            "extra-phone-root": [
                *valid_nodes,
                DRIVER.UiNode(
                    {
                        "resource-id": "phone-destination-experimental",
                        "text": "Experimental",
                        "clickable": "true",
                    }
                ),
            ],
            "tablet-root": [
                *valid_nodes,
                DRIVER.UiNode(
                    {
                        "resource-id": "tablet-destination-tablet-home",
                        "text": "Home",
                        "clickable": "true",
                    }
                ),
            ],
            "postponed-page-id": [
                *valid_nodes,
                DRIVER.UiNode(
                    {
                        "resource-id": "phone-play-unavailable",
                        "text": "Unavailable",
                    }
                ),
            ],
            "support-description": [
                *valid_nodes,
                DRIVER.UiNode(
                    {
                        "resource-id": "live-support",
                        "content-desc": "Open Tough Tongue support",
                        "clickable": "true",
                    }
                ),
            ],
            "forbidden-action-launcher": [
                *valid_nodes,
                DRIVER.UiNode(
                    {
                        "resource-id": "more-all-actions",
                        "text": "All actions",
                        "clickable": "true",
                    }
                ),
            ],
        }

        for name, nodes in adversarial_nodes.items():
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                device = Mock(spec=DRIVER.Device)
                device.evidence = Path(temporary)
                device.display_size.return_value = (1080, 2400)
                device.hierarchy.return_value = nodes
                with self.assertRaisesRegex(RuntimeError, "postponed surface"):
                    DRIVER.assert_phone_shell_surface(
                        device,
                        route_resource_id="phone-more",
                        evidence_prefix=name,
                    )
                device.capture.assert_called_once_with(f"{name}-invalid")

    def test_phone_shell_observation_rejects_device_visible_postponed_surfaces(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.display_size.return_value = (1080, 2400)
            device.hierarchy.return_value = [
                *self.native_phone_tabs(),
                DRIVER.UiNode(
                    {
                        "resource-id": "phone-destination-play",
                        "text": "Play",
                        "clickable": "true",
                    }
                ),
                DRIVER.UiNode(
                    {
                        "resource-id": "rook-launch",
                        "text": "Ask Rook",
                        "clickable": "true",
                    }
                ),
            ]

            with self.assertRaisesRegex(RuntimeError, "postponed surface"):
                DRIVER.assert_phone_shell_surface(
                    device,
                    route_resource_id="phone-runner-sheet",
                    evidence_prefix="career-shell",
                )

            device.capture.assert_called_once_with("career-shell-invalid")

    def test_phone_shell_observation_allows_clickable_workspace_entries_with_postponed_names(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.display_size.return_value = (1080, 2400)
            device.hierarchy.return_value = [
                *self.native_phone_tabs(),
                *[
                    DRIVER.UiNode(
                        {
                            "resource-id": f"open-workspace-{token}",
                            "text": label,
                            "clickable": "true",
                        }
                    )
                    for token, label in (
                        ("rook", "Rook"),
                        ("campaign", "Campaign"),
                        ("play", "Play"),
                        ("table", "Table"),
                        ("all-actions", "All actions"),
                    )
                ],
                DRIVER.UiNode(
                    {
                        "resource-id": "character-notes",
                        "text": "Ask about Tough Tongue after this run.",
                        "clickable": "false",
                    }
                ),
            ]

            observed = DRIVER.assert_phone_shell_surface(
                device,
                route_resource_id="phone-runner-sheet",
                evidence_prefix="runner-text",
            )

            self.assertEqual([], observed["forbiddenSupportLabels"])
            self.assertEqual([], observed["forbiddenRouteResourceIds"])
            device.capture.assert_not_called()

    def test_phone_initial_route_requires_profile_and_workspace_identity(self) -> None:
        source = (
            REPO_ROOT / "src" / "Chummer.Android" / "MainShell.cs"
        ).read_text(encoding="utf-8")
        block = source[source.index("private async Task ResolveInitialPhoneRouteAsync") :]
        block = block[: block.index("private void BuildTabletShell")]

        self.assertIn(
            "if (coordinator.State.Profile is not null\n"
            "                && coordinator.State.WorkspaceId is not null)",
            block,
        )
        self.assertLess(
            block.index("coordinator.State.WorkspaceId is not null"),
            block.index("GoToAsync(PhoneShellRoutes.RunnerAbsolute)"),
        )
        self.assertIn("else\n            {\n                await GoToAsync(PhoneShellRoutes.RunnersAbsolute);", block)
        self.assertNotIn(
            "if (coordinator.State.Profile is not null)\n"
            "            {\n"
            "                await GoToAsync",
            block,
        )

    def test_cancelled_new_runner_cannot_route_from_a_stale_existing_profile(self) -> None:
        source = (
            REPO_ROOT / "src" / "Chummer.Android" / "Native" / "HomePage.cs"
        ).read_text(encoding="utf-8")
        handler = source[source.index('create.AutomationId = "home-new-runner"') :]
        handler = handler[: handler.index("quick.Add(open)")]

        self.assertIn(
            "create.Clicked += async (_, _) => await RunAsync(() => "
            "Coordinator.CreateRunnerAsync());",
            handler,
        )
        self.assertNotIn("Coordinator.State.Profile", handler)
        self.assertNotIn("Coordinator.State.WorkspaceId", handler)
        self.assertNotIn("NativeWorkspaceActivationReceipt", handler)
        self.assertNotIn("GoToAsync", handler)

    def test_phone_route_migration_covers_drivers_without_rewriting_tablet_flow(self) -> None:
        shared_path = Path(DRIVER.__file__).resolve()
        migrated: list[str] = []
        for path in sorted((REPO_ROOT / "tests").glob("run_api36*_e2e.py")):
            source = path.read_text(encoding="utf-8")
            if path == shared_path:
                continue
            if "shared.wait_for_phone_" not in source:
                continue
            migrated.append(path.name)
            self.assertNotIn('device.wait("Continue building"', source, path.name)
            self.assertNotIn('device.wait("Runner"', source, path.name)
            self.assertNotIn('device.wait("Your runners"', source, path.name)
            self.assertNotIn('device.wait("Home"', source, path.name)
            self.assertNotIn('device.tap("Home"', source, path.name)

        self.assertGreaterEqual(len(migrated), 80)
        shared_source = shared_path.read_text(encoding="utf-8")
        self.assertIn('if args.profile == "phone":\n        wait_for_phone_runners(device)', shared_source)
        self.assertIn('else:\n        device.wait("Your runners", timeout=90)', shared_source)
        self.assertIn('if profile == "tablet":', shared_source)
        self.assertIn('device.tap("Build")', shared_source)
        self.assertIn('device.wait("tablet-build-layout", timeout=45)', shared_source)

    def test_phone_shell_destinations_are_never_tapped_by_ambiguous_text(self) -> None:
        drivers = sorted((REPO_ROOT / "tests").glob("run_api36*_e2e.py"))
        self.assertGreaterEqual(len(drivers), 80)
        for path in drivers:
            source = path.read_text(encoding="utf-8")
            for label in ("Runner", "Runners", "More"):
                for ambiguous_call in (
                    f'device.tap("{label}"',
                    f"device.tap('{label}'",
                ):
                    self.assertNotIn(ambiguous_call, source, path.name)

        shared_source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        self.assertIn("def tap_phone_destination(", shared_source)
        self.assertIn("bind_phone_shell_destinations(", shared_source)
        self.assertIn("wait_for_phone_shell_destinations(", shared_source)
        self.assertNotIn(
            "device.tap_single_exact_resource_id(\n        resource_id,",
            shared_source,
        )
        for resource_id in DRIVER.PHONE_SHELL_DESTINATION_IDS:
            self.assertIn(f'"{resource_id}"', shared_source)

    def test_launch_app_uses_exact_main_launcher_and_requires_resumed_activity(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.shell.side_effect = [
                "package:/data/app/exact/base.apk",
                component,
                "7225",
                f"mResumedActivity: ActivityRecord{{123 u0 {component} t9}}",
            ]
            device.run.side_effect = [
                subprocess.CompletedProcess(args=["logcat", "-c"], returncode=0, stdout="", stderr=""),
                self.successful_am_start(component),
            ]

            state = DRIVER.launch_app(device, resume_timeout=0)

            verified = Path(temporary) / "launch-attempt-1-verified.txt"
            self.assertTrue(verified.is_file())
            self.assertIn("process_ids=7225", verified.read_text(encoding="utf-8"))
            self.assertIn(f"resumed_component={component}", verified.read_text(encoding="utf-8"))
            self.assertEqual(("7225",), state.process_ids)
            self.assertEqual(component, state.resumed_component)

        self.assertEqual(
            call(
                "shell",
                "am",
                "start",
                "--user",
                "current",
                "-W",
                "-a",
                DRIVER.MAIN_ACTION,
                "-c",
                DRIVER.LAUNCHER_CATEGORY,
                "--ez",
                DRIVER.E2E_AUTHORITY_EXTRA,
                "true",
                "-n",
                component,
                timeout=60,
                check=False,
            ),
            device.run.call_args_list[1],
        )
        self.assertNotIn("-S", device.run.call_args_list[1].args)

    def test_workspace_authority_opt_in_is_one_typed_boolean_extra(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        arguments = DRIVER.workspace_authority_start_arguments(component)

        self.assertEqual(1, arguments.count("--ez"))
        self.assertNotIn("--es", arguments)
        self.assertEqual(1, arguments.count(DRIVER.E2E_AUTHORITY_EXTRA))
        extra_index = arguments.index("--ez")
        self.assertEqual(
            (
                "--ez",
                DRIVER.E2E_AUTHORITY_EXTRA,
                "true",
            ),
            arguments[extra_index : extra_index + 3],
        )
        self.assertEqual(("-n", component), arguments[-2:])

        with self.assertRaisesRegex(RuntimeError, "not canonical"):
            DRIVER.workspace_authority_start_arguments(
                "com.myexternalbrain.chummer/.MainActivity"
            )

    def test_launch_app_accepts_am_wait_timeout_only_with_exact_process_and_resume(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        timeout = subprocess.TimeoutExpired(
            ["adb", "shell", "am", "start", "-W"],
            60,
            output="Status: ok\n",
            stderr="",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.shell.side_effect = [
                "package:/data/app/exact/base.apk",
                component,
                "7225",
                f"topResumedActivity=ActivityRecord{{123 u0 {component} t9}}",
            ]
            device.run.side_effect = [
                subprocess.CompletedProcess(args=["logcat", "-c"], returncode=0, stdout="", stderr=""),
                timeout,
            ]

            DRIVER.launch_app(device, resume_timeout=0)

        self.assertEqual(2, device.run.call_count)

    def test_launch_failure_captures_command_activity_window_and_all_log_buffers(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        launcher = "com.google.android.apps.nexuslauncher/.NexusLauncherActivity"
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            device.capture.return_value = None
            device.shell.side_effect = [
                "package:/data/app/exact/base.apk",
                component,
                "7225",
                f"mResumedActivity: ActivityRecord{{456 u0 {launcher} t2}}",
                f"mCurrentFocus=Window{{456 u0 {launcher}}}",
                "ApplicationExitInfo(timestamp=1, reason=CRASH)",
            ]
            device.run.side_effect = [
                subprocess.CompletedProcess(args=["logcat", "-c"], returncode=0, stdout="", stderr=""),
                self.successful_am_start(component),
                subprocess.CompletedProcess(
                    args=["logcat", "-d"],
                    returncode=0,
                    stdout=(
                        "AndroidRuntime: FATAL EXCEPTION: main\n"
                        "AndroidRuntime: Process: com.myexternalbrain.chummer, PID: 7225\n"
                    ),
                    stderr="",
                ),
                subprocess.CompletedProcess(
                    args=["logcat", "-d", "-b", "events"],
                    returncode=0,
                    stdout="am_crash: com.myexternalbrain.chummer\n",
                    stderr="",
                ),
                subprocess.CompletedProcess(
                    args=["logcat", "-d", "-b", "crash"],
                    returncode=0,
                    stdout="AndroidRuntime: Process: com.myexternalbrain.chummer\n",
                    stderr="",
                ),
            ]

            with self.assertRaisesRegex(RuntimeError, "did not remain the exact resumed activity"):
                DRIVER.launch_app(device, resume_timeout=0)

            evidence = Path(temporary)
            self.assertIn("Status: ok", (evidence / "launch-attempt-1-am-start.stdout.txt").read_text())
            self.assertIn(launcher, (evidence / "launch-attempt-1-activity.txt").read_text())
            self.assertIn("FATAL EXCEPTION", (evidence / "launch-attempt-1-logcat.txt").read_text())
            self.assertIn("reason=CRASH", (evidence / "launch-attempt-1-exit-info.txt").read_text())
            self.assertIn("am_crash", (evidence / "launch-attempt-1-logcat-events.txt").read_text())
            self.assertIn("AndroidRuntime", (evidence / "launch-attempt-1-logcat-crash.txt").read_text())
            device.capture.assert_called_once_with("launch-attempt-1-failure")

        self.assertIn(
            call(
                "logcat",
                "-d",
                "-b",
                "all",
                "-v",
                "threadtime",
                "-t",
                "4000",
                timeout=60,
                check=False,
            ),
            device.run.call_args_list,
        )
        for buffer_name in ("events", "crash"):
            self.assertIn(
                call(
                    "logcat",
                    "-d",
                    "-b",
                    buffer_name,
                    "-v",
                    "threadtime",
                    timeout=60,
                    check=False,
                ),
                device.run.call_args_list,
            )

    def test_launcher_component_uses_current_package_manager_result(self) -> None:
        device = Mock()
        device.shell.side_effect = [
            "package:/data/app/exact/base.apk",
            "priority=0\ncom.myexternalbrain.chummer/crccurrent.MainActivity\n",
        ]

        self.assertEqual(
            "com.myexternalbrain.chummer/crccurrent.MainActivity",
            DRIVER.launcher_component(device),
        )
        self.assertEqual(
            call(
                "cmd",
                "package",
                "resolve-activity",
                "--brief",
                "--user",
                "current",
                "-a",
                DRIVER.MAIN_ACTION,
                "-c",
                DRIVER.LAUNCHER_CATEGORY,
                "-p",
                DRIVER.PACKAGE,
            ),
            device.shell.call_args_list[1],
        )

    def test_process_restart_requires_a_new_exact_launch_pid(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        stopped = DRIVER.LaunchState((), None, "stopped")
        restarted = DRIVER.LaunchState(("7351",), component, "restart")
        with tempfile.TemporaryDirectory() as temporary:
            device = Mock(spec=DRIVER.Device)
            device.evidence = Path(temporary)
            with patch.object(
                DRIVER,
                "current_launch_state",
                side_effect=[initial, stopped],
            ), patch.object(DRIVER, "launch_app", return_value=restarted):
                actual = DRIVER.force_stop_and_launch_new_process(device, initial)

            proof = Path(temporary) / "process-restart-verified.txt"
            self.assertTrue(proof.is_file())
            self.assertIn("pre_force_stop_process_ids=7225", proof.read_text(encoding="utf-8"))
            self.assertIn("post_force_stop_process_ids=", proof.read_text(encoding="utf-8"))
            self.assertIn("restart_process_ids=7351", proof.read_text(encoding="utf-8"))

        self.assertIs(initial, actual.before_force_stop)
        self.assertIs(stopped, actual.after_force_stop)
        self.assertIs(restarted, actual.restarted)
        device.shell.assert_called_once_with("am", "force-stop", DRIVER.PACKAGE)

    def test_process_restart_rejects_a_reused_pid(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        stopped = DRIVER.LaunchState((), None, "stopped")
        restarted = DRIVER.LaunchState(("7225",), component, "restart")
        device = Mock(spec=DRIVER.Device)
        with patch.object(
            DRIVER,
            "current_launch_state",
            side_effect=[initial, stopped],
        ), patch.object(DRIVER, "launch_app", return_value=restarted):
            with self.assertRaisesRegex(RuntimeError, "reused an existing PID"):
                DRIVER.force_stop_and_launch_new_process(device, initial)

        device.capture.assert_called_once_with("process-restart-pid-reused")

    def test_process_restart_rejects_a_nonempty_post_force_stop_pid_set(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        still_running = DRIVER.LaunchState(("7225",), None, "stopping")
        device = Mock(spec=DRIVER.Device)
        with patch.object(
            DRIVER,
            "current_launch_state",
            side_effect=[initial, still_running],
        ), patch.object(DRIVER, "launch_app") as launch:
            with self.assertRaisesRegex(RuntimeError, "remained non-empty"):
                DRIVER.force_stop_and_launch_new_process(device, initial)

        launch.assert_not_called()
        device.capture.assert_called_once_with("process-restart-force-stop-not-empty")

    def test_process_restart_rejects_a_changed_live_launch_identity(self) -> None:
        component = "com.myexternalbrain.chummer/crccurrent.MainActivity"
        initial = DRIVER.LaunchState(("7225",), component, "initial")
        changed = DRIVER.LaunchState(("7300",), component, "changed")
        device = Mock(spec=DRIVER.Device)
        with patch.object(DRIVER, "current_launch_state", return_value=changed), patch.object(
            DRIVER,
            "launch_app",
        ) as launch:
            with self.assertRaisesRegex(RuntimeError, "changed before the owned force-stop"):
                DRIVER.force_stop_and_launch_new_process(device, initial)

        launch.assert_not_called()
        device.shell.assert_not_called()
        device.capture.assert_called_once_with("process-restart-precondition-changed")

    def test_launcher_component_rejects_missing_package_or_ambiguous_component(self) -> None:
        missing = Mock()
        missing.shell.return_value = ""
        with self.assertRaisesRegex(RuntimeError, "not installed"):
            DRIVER.launcher_component(missing)

        ambiguous = Mock()
        ambiguous.shell.side_effect = [
            "package:/data/app/exact/base.apk",
            (
                "com.myexternalbrain.chummer/crc.one.MainActivity\n"
                "com.myexternalbrain.chummer/crc.two.MainActivity\n"
            ),
        ]
        with self.assertRaisesRegex(RuntimeError, "exactly one"):
            DRIVER.launcher_component(ambiguous)

    def test_resumed_activity_parser_normalizes_short_activity_and_ignores_focus(self) -> None:
        dump = (
            "mCurrentFocus=Window{123 u0 com.example/.Other}\n"
            "topResumedActivity=ActivityRecord{456 u0 "
            "com.myexternalbrain.chummer/.MainActivity t4}\n"
        )
        self.assertEqual(
            "com.myexternalbrain.chummer/com.myexternalbrain.chummer.MainActivity",
            DRIVER.resumed_activity(dump),
        )

    def test_hierarchy_ignores_uiautomator_preamble(self) -> None:
        output = (
            "UI hierarchy dumped to: /sdcard/window.xml\n"
            "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>"
            "<hierarchy><node text='Your runners' /></hierarchy>"
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = FakeDevice(Path(temporary), output)
            nodes = device.hierarchy()

        self.assertEqual("Your runners", nodes[0].attributes["text"])

    def test_hierarchy_uses_compressed_android_dump(self) -> None:
        xml = "<hierarchy><node text='Your runners' /></hierarchy>"
        device = Mock(spec=DRIVER.Device)
        device.shell.return_value = "UI hierarchy dumped"
        device.run.return_value = subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=xml,
            stderr="",
        )
        with tempfile.TemporaryDirectory() as temporary:
            device.evidence = Path(temporary)
            nodes = DRIVER.Device.hierarchy(device)

        self.assertEqual("Your runners", nodes[0].attributes["text"])
        device.shell.assert_called_once_with(
            "uiautomator",
            "dump",
            "--compressed",
            "/sdcard/chummer-editing-window.xml",
        )

    def test_invalid_hierarchy_is_retryable_and_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = FakeDevice(evidence, "ERROR: could not get idle state")
            self.assertEqual([], device.hierarchy())
            diagnostic = evidence / "last-invalid-hierarchy.txt"
            self.assertTrue(diagnostic.is_file())
            self.assertIn("could not get idle state", diagnostic.read_text(encoding="utf-8"))

    def test_failed_dump_status_does_not_reuse_stale_hierarchy_xml(self) -> None:
        stale = "<hierarchy><node text='Previous screen' /></hierarchy>"
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = FakeDevice(
                evidence,
                stale,
                dump_output="ERROR: could not get idle state.",
            )

            self.assertEqual([], device.hierarchy())
            diagnostic = evidence / "last-invalid-hierarchy.txt"
            self.assertIn(
                "could not get idle state",
                diagnostic.read_text(encoding="utf-8"),
            )

    def test_android_hierchary_success_typo_is_accepted(self) -> None:
        xml = "<hierarchy><node text='Current screen' /></hierarchy>"
        with tempfile.TemporaryDirectory() as temporary:
            device = FakeDevice(
                Path(temporary),
                xml,
                dump_output="UI hierchary dumped to: /sdcard/window.xml",
            )

            nodes = device.hierarchy()

        self.assertEqual("Current screen", nodes[0].attributes["text"])

    def test_swipe_up_stays_inside_tablet_display(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 2560x1800")
            device.swipe_up()

        self.assertEqual(
            ("input", "swipe", "1280", "1476", "1280", "540", "300"),
            device.commands[-1],
        )

    def test_small_swipe_keeps_overlap_for_long_dialogs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.swipe_up(distance_ratio=0.28)

        self.assertEqual(
            ("input", "swipe", "540", "1968", "540", "1296", "300"),
            device.commands[-1],
        )

    def test_bidirectional_tap_resets_without_hierarchy_probes(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": "build-section-tab-attributes",
                "clickable": "true",
                "bounds": "[100,400][900,520]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device.find.return_value = target
        device.dismiss_system_ui_anr.return_value = False
        device._scroll_x_ratio.return_value = 0.5
        device.node_has_tappable_bounds.return_value = True

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-attributes",
                backward_scrolls=4,
                forward_scrolls=4,
            )

        self.assertEqual(
            [call(x_ratio=0.5, distance_ratio=0.22)] * 4,
            device.swipe_down.call_args_list,
        )
        device.find.assert_called_once_with("build-section-tab-attributes")
        device.swipe_up.assert_not_called()
        device.shell.assert_called_once_with("input", "tap", "500", "460")

    def test_bidirectional_tap_has_bounded_forward_fallback(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": "build-section-tab-attributes",
                "clickable": "true",
                "bounds": "[100,400][900,520]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device.find.side_effect = [None, None, target]
        device.dismiss_system_ui_anr.return_value = False
        device._scroll_x_ratio.return_value = 0.5
        device.node_has_tappable_bounds.return_value = True

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-attributes",
                backward_scrolls=1,
                forward_scrolls=2,
            )

        device.swipe_down.assert_called_once_with(x_ratio=0.5, distance_ratio=0.22)
        self.assertEqual(
            [call(x_ratio=0.5, distance_ratio=0.22)] * 2,
            device.swipe_up.call_args_list,
        )

    def test_bidirectional_tap_reaches_hidden_target_with_expensive_hierarchy_dumps(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": "build-section-tab-attributes",
                "clickable": "true",
                "bounds": "[100,400][900,520]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device._scroll_x_ratio.return_value = 0.5
        device.node_has_tappable_bounds.return_value = True
        elapsed = [0.0]
        down_find_counts: list[int] = []

        def expensive_find(selector: str) -> DRIVER.UiNode | None:
            elapsed[0] += 4.0
            if (
                selector == "build-section-tab-attributes"
                and device.swipe_up.call_count >= 12
            ):
                return target
            return None

        device.find.side_effect = expensive_find
        device.swipe_down.side_effect = lambda **_kwargs: down_find_counts.append(
            device.find.call_count
        )
        device.dismiss_system_ui_anr.side_effect = (
            lambda: DRIVER.Device.dismiss_system_ui_anr(device)
        )

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=lambda: elapsed[0]),
            patch.object(DRIVER.time, "sleep"),
        ):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-attributes",
                timeout=120,
                backward_scrolls=24,
                forward_scrolls=24,
            )

        self.assertEqual([0] * 24, down_find_counts)
        self.assertEqual(25, device.find.call_count)
        self.assertEqual(12, device.dismiss_system_ui_anr.call_count)
        self.assertEqual(24, device.swipe_down.call_count)
        self.assertEqual(12, device.swipe_up.call_count)
        device.shell.assert_called_once_with("input", "tap", "500", "460")

    def test_bidirectional_tap_keeps_forward_search_bounded_when_target_is_absent(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.find.return_value = None
        device.dismiss_system_ui_anr.return_value = False
        device._scroll_x_ratio.return_value = 0.5

        with patch.object(DRIVER.time, "sleep"):
            with self.assertRaisesRegex(RuntimeError, "bounded bidirectional search"):
                DRIVER.Device.tap_bidirectional(
                    device,
                    "build-section-tab-attributes",
                    timeout=120,
                    backward_scrolls=3,
                    forward_scrolls=2,
                )

        self.assertEqual(3, device.swipe_down.call_count)
        self.assertEqual(2, device.swipe_up.call_count)
        self.assertEqual(3, device.find.call_count)
        device.capture.assert_called_once_with("failure")

    def test_exact_bidirectional_tap_recovers_when_twelve_swipes_leave_gear_clipped(self) -> None:
        clipped = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/build-section-tab-gear",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        target = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/build-section-tab-gear",
                "clickable": "true",
                "bounds": "[98,400][984,560]",
            }
        )
        prefix_decoy = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/build-section-tab-gear-locations"
                ),
                "clickable": "true",
                "bounds": "[98,700][984,860]",
            }
        )
        legacy = Mock(spec=DRIVER.Device)
        legacy_position = {"forward_swipes": 0}
        legacy_clock = {"seconds": 0}
        legacy._scroll_x_ratio.return_value = 0.5
        legacy.display_size.return_value = (1080, 2400)
        legacy.hierarchy.side_effect = lambda: [
            prefix_decoy,
            *([clipped] if legacy_position["forward_swipes"] == 1 else []),
        ]
        legacy.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(legacy, selector)
        )
        legacy.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(legacy, node)
        )
        legacy.dismiss_system_ui_anr.return_value = False
        legacy.swipe_up.side_effect = lambda **_: legacy_position.update(
            forward_swipes=legacy_position["forward_swipes"] + 1
        )

        def advance_legacy_clock() -> int:
            legacy_clock["seconds"] += 1
            return legacy_clock["seconds"]

        with (
            patch.object(DRIVER.time, "monotonic", side_effect=advance_legacy_clock),
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "Timed out waiting for tappable UI node"),
        ):
            DRIVER.Device.tap(
                legacy,
                "build-section-tab-gear",
                timeout=8,
                scroll=True,
                max_scrolls=6,
                scroll_distance_ratio=0.52,
                exact_resource_id=True,
            )

        self.assertEqual(
            [call(x_ratio=0.5, distance_ratio=0.52)] * 6,
            legacy.swipe_up.call_args_list,
        )
        legacy.capture.assert_called_once_with("failure")
        legacy.node_has_tappable_bounds.assert_called_once_with(clipped)
        legacy.shell.assert_not_called()

        device = Mock(spec=DRIVER.Device)
        position = {"rows_below_top": 24}
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.swipe_down.side_effect = lambda **_: position.update(
            rows_below_top=max(0, position["rows_below_top"] - 1)
        )
        device.swipe_up.side_effect = lambda **_: position.update(
            rows_below_top=position["rows_below_top"] + 1
        )
        device.hierarchy.side_effect = lambda: [
            prefix_decoy,
            *(
                [clipped]
                if position["rows_below_top"] == 12
                else [target]
                if position["rows_below_top"] == 11
                else []
            ),
        ]
        device.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(device, selector)
        )
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        for _ in range(12):
            device.swipe_down(x_ratio=0.5, distance_ratio=0.22)
        twelve_swipe_candidate = DRIVER.Device.find_exact_resource_id(
            device,
            "build-section-tab-gear",
        )
        self.assertIs(clipped, twelve_swipe_candidate)
        self.assertFalse(DRIVER.Device.node_has_tappable_bounds(device, clipped))
        self.assertTrue(DRIVER.Device._matches(prefix_decoy, "build-section-tab-gear"))

        position["rows_below_top"] = 24
        device.reset_mock()
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(device, selector)
        )
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.Device.tap_bidirectional(
                device,
                "build-section-tab-gear",
                timeout=120,
                backward_scrolls=24,
                forward_scrolls=24,
                scroll_distance_ratio=0.22,
                exact_resource_id=True,
            )

        self.assertEqual(24, device.swipe_down.call_count)
        self.assertEqual(11, device.swipe_up.call_count)
        self.assertEqual(11, position["rows_below_top"])
        device.find.assert_not_called()
        device.shell.assert_called_once_with("input", "tap", "541", "480")

    def test_exact_bidirectional_tap_exhaustion_never_taps_clipped_or_prefix_decoy(self) -> None:
        clipped = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/build-section-tab-gear",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        prefix_decoy = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/build-section-tab-gear-locations"
                ),
                "clickable": "true",
                "bounds": "[98,700][984,860]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.hierarchy.return_value = [prefix_decoy, clipped]
        device.find_exact_resource_id.side_effect = (
            lambda selector: DRIVER.Device.find_exact_resource_id(device, selector)
        )
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"):
            with self.assertRaisesRegex(RuntimeError, "bounded bidirectional search"):
                DRIVER.Device.tap_bidirectional(
                    device,
                    "build-section-tab-gear",
                    backward_scrolls=2,
                    forward_scrolls=2,
                    exact_resource_id=True,
                )

        self.assertEqual(2, device.swipe_down.call_count)
        self.assertEqual(2, device.swipe_up.call_count)
        device.find.assert_not_called()
        device.capture.assert_called_once_with("failure")
        device.shell.assert_not_called()

    def test_fixture_transport_verifies_the_exact_remote_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            local = Path(temporary) / "runner.chum5"
            local.write_bytes(b"exact fixture")
            expected = DRIVER.sha256(local)
            device = Mock(spec=DRIVER.Device)
            device.shell.return_value = f"{expected}  /sdcard/Download/runner.chum5"

            actual = DRIVER.Device.push_verified(
                device,
                local,
                "/sdcard/Download/runner.chum5",
                expected,
            )

            self.assertEqual(expected, actual)
            device.push.assert_called_once_with(
                local.resolve(),
                "/sdcard/Download/runner.chum5",
            )
            device.shell.assert_called_once_with(
                "sha256sum",
                "/sdcard/Download/runner.chum5",
            )

    def test_fixture_transport_rejects_changed_remote_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            local = Path(temporary) / "runner.chum5"
            local.write_bytes(b"exact fixture")
            expected = DRIVER.sha256(local)
            device = Mock(spec=DRIVER.Device)
            device.shell.return_value = f"{'b' * 64}  /sdcard/Download/runner.chum5"

            with self.assertRaisesRegex(RuntimeError, "transport digest mismatch"):
                DRIVER.Device.push_verified(
                    device,
                    local,
                    "/sdcard/Download/runner.chum5",
                    expected,
                )

    def test_collection_tap_uses_card_gutter_and_overlapping_scrolls(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "text": "Ares Predator V",
                        "clickable": "false",
                        "bounds": "[100,500][700,560]",
                    }
                )
            ]
            DRIVER.tap_collection_item(device, "Ares Predator V")

        self.assertEqual(("input", "tap", "82", "530"), device.commands[-1])

    def test_system_ui_anr_is_captured_and_fails_without_dismissal(self) -> None:
        device = Mock(spec=DRIVER.Device)
        wait_button = DRIVER.UiNode(
            {
                "resource-id": "android:id/aerr_wait",
                "text": "Wait",
                "clickable": "true",
                "bounds": "[100,1200][900,1400]",
            }
        )

        with self.assertRaisesRegex(
            DRIVER.ProductAnrDetected,
            "refused to dismiss the dialog as success",
        ):
            DRIVER.Device.dismiss_system_ui_anr(device, [wait_button])

        device.capture_product_anr_evidence.assert_called_once_with()
        device.shell.assert_not_called()

    def test_product_anr_diagnostics_are_read_only_and_do_not_signal_process(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = Mock(spec=DRIVER.Device)
            device.evidence = evidence
            device.run.return_value = subprocess.CompletedProcess(
                args=[], returncode=0, stdout=b"exact screenshot", stderr=b""
            )

            def shell(*arguments: str, **_kwargs: object) -> str:
                if arguments == ("pidof", DRIVER.PACKAGE):
                    return "3105"
                return "diagnostic output"

            device.shell.side_effect = shell

            DRIVER.Device.capture_product_anr_evidence(device)

        self.assertNotIn(
            call("kill", "-3", "3105", timeout=15),
            device.shell.call_args_list,
        )
        self.assertEqual(
            ("pidof", DRIVER.PACKAGE),
            device.shell.call_args_list[0].args,
        )
        self.assertEqual(
            [
                ("dumpsys", "activity", "lastanr"),
                ("dumpsys", "activity", "processes"),
                ("dumpsys", "activity", "exit-info", DRIVER.PACKAGE),
                ("dumpsys", "window", "windows"),
                ("ls", "-la", "/data/anr"),
                ("logcat", "-d", "-b", "all", "-v", "threadtime", "-t", "4000"),
            ],
            [entry.args for entry in device.shell.call_args_list[1:]],
        )

    def test_wait_hard_fails_on_anr_without_scrolling_or_retrying(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.find.return_value = None
        device.dismiss_system_ui_anr.side_effect = DRIVER.ProductAnrDetected(
            "captured product ANR"
        )
        device._scroll_x_ratio.return_value = 0.5

        with self.assertRaisesRegex(DRIVER.ProductAnrDetected, "captured product ANR"):
            DRIVER.Device.wait(
                device,
                "ContactE2E",
                scroll=True,
                max_scrolls=8,
                scroll_distance_ratio=0.22,
            )

        device.find.assert_called_once_with("ContactE2E")
        device.swipe_up.assert_not_called()
        device.capture.assert_not_called()

    def test_new_runner_launch_uses_exact_resource_ids_not_localized_dialog_text(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        self.assertIn(
            'device.tap_exact_resource_id_until_exact_resource_id(\n'
            '        "home-new-runner",\n'
            '        "dialog-action-create-character",',
            source,
        )
        self.assertIn("time.sleep(1.25)", source)

    def test_exact_resource_transition_scrolls_one_exact_target_surface(self) -> None:
        device = Mock(spec=DRIVER.Device)
        source = DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/home-new-runner",
                "clickable": "true",
                "bounds": "[40,400][1040,560]",
            }
        )
        surface = DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/dialog-surface",
                "bounds": "[0,275][1080,2400]",
            }
        )
        target = DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/dialog-action-create-character",
                "clickable": "true",
                "bounds": "[53,2080][1028,2200]",
            }
        )
        device.hierarchy.side_effect = [[source], [surface], [target]]
        device.node_has_tappable_bounds.return_value = True
        device.display_size.return_value = (1080, 2400)
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"):
            actual = DRIVER.Device.tap_exact_resource_id_until_exact_resource_id(
                device,
                "home-new-runner",
                "dialog-action-create-character",
                target_scroll_surface="dialog-surface",
                max_target_scrolls=4,
            )

        self.assertIs(target, actual)
        device.shell.assert_called_once_with("input", "tap", "540", "480")
        device.swipe_up.assert_called_once_with(
            x_ratio=0.5,
            distance_ratio=0.22,
        )
        device.capture.assert_not_called()

    def test_exact_resource_transition_bounds_target_surface_scrolling(self) -> None:
        device = Mock(spec=DRIVER.Device)
        source = DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/home-new-runner",
                "clickable": "true",
                "bounds": "[40,400][1040,560]",
            }
        )
        surface = DRIVER.UiNode(
            {
                "resource-id": f"{DRIVER.PACKAGE}:id/dialog-surface",
                "bounds": "[0,275][1080,2400]",
            }
        )
        device.hierarchy.side_effect = [[source], [surface], [surface]]
        device.node_has_tappable_bounds.return_value = True
        device.display_size.return_value = (1080, 2400)
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "Timed out waiting for exact create-character build-method action",
        ):
            DRIVER.Device.tap_exact_resource_id_until_exact_resource_id(
                device,
                "home-new-runner",
                "dialog-action-create-character",
                evidence_prefix="new-runner-build-method-dialog",
                target_name="Create-character build-method action",
                target_scroll_surface="dialog-surface",
                max_target_scrolls=1,
            )

        device.swipe_up.assert_called_once_with(
            x_ratio=0.5,
            distance_ratio=0.22,
        )
        device.capture.assert_called_once_with(
            "new-runner-build-method-dialog-target-unavailable"
        )

    def test_full_phone_journey_proves_wizard_then_imports_completed_runner(self) -> None:
        device = Mock()
        fixture_sha256 = "a" * 64
        creation = DRIVER.WorkspaceAuthority("creation", 1, 1, "b" * 64, "c" * 64)
        imported = DRIVER.WorkspaceAuthority("imported", 2, 2, fixture_sha256, "d" * 64)

        with patch.object(DRIVER, "select_android_document") as select_document, \
             patch.object(DRIVER, "open_creation_dashboard") as open_dashboard, \
             patch.object(DRIVER, "wait_for_phone_runner_route") as wait_route, \
             patch.object(DRIVER, "tap_phone_destination") as tap_destination, \
             patch.object(
                 DRIVER,
                 "read_workspace_authority",
                 side_effect=[creation, imported],
             ):
            result = DRIVER.prepare_full_editing_runner(
                device,
                "phone",
                "career-full-editing-e2e.chum5",
                "FullEditingE2E",
                fixture_sha256,
            )

        device.assert_has_calls(
            [
                call.tap_exact_resource_id_until_exact_resource_id(
                    "home-new-runner",
                    "dialog-action-create-character",
                    evidence_prefix="new-runner-build-method-dialog",
                    source_name="New runner control",
                    target_name="Create-character build-method action",
                    target_scroll_surface="dialog-surface",
                    max_target_scrolls=16,
                ),
                call.tap("dialog-action-create-character", scroll=True),
                call.capture("new-runner-creation-wizard"),
                call.tap("build-save-runner"),
                call.wait(
                    "Saved.",
                    timeout=90,
                    scroll=True,
                    max_scrolls=48,
                    scroll_distance_ratio=0.22,
                ),
                call.wait_for_single_exact_resource_id(
                    "phone-runners",
                    timeout=90,
                    evidence_prefix="phone-runners-route",
                    surface_name="Phone runners route",
                ),
                call.wait("home-open-file", timeout=90),
                call.tap("home-open-file"),
                call.wait("FullEditingE2E", timeout=90),
                call.wait_for_single_exact_resource_id(
                    "phone-runners",
                    timeout=90,
                    evidence_prefix="phone-runners-route",
                    surface_name="Phone runners route",
                ),
            ]
        )
        select_document.assert_called_once_with(
            device,
            "career-full-editing-e2e.chum5",
        )
        open_dashboard.assert_called_once_with(
            device,
            "phone",
            open_build_route=False,
        )
        self.assertEqual(
            [call(device, created=False), call(device, created=True)],
            wait_route.call_args_list,
        )
        self.assertEqual(
            [
                call(device, "phone-destination-runners"),
                call(device, "phone-destination-runners"),
            ],
            tap_destination.call_args_list,
        )
        self.assertEqual(imported, result)

    def test_full_tablet_journey_remains_available_outside_the_phone_beta_lane(self) -> None:
        device = Mock()

        with patch.object(DRIVER, "select_android_document") as select_document:
            result = DRIVER.prepare_full_editing_runner(
                device,
                "tablet",
                "career-full-editing-e2e.chum5",
                "FullEditingE2E",
                "a" * 64,
            )

        device.assert_has_calls(
            [
                call.tap_exact_resource_id_until_exact_resource_id(
                    "home-new-runner",
                    "dialog-action-create-character",
                    evidence_prefix="new-runner-build-method-dialog",
                    source_name="New runner control",
                    target_name="Create-character build-method action",
                    target_scroll_surface="dialog-surface",
                    max_target_scrolls=16,
                ),
                call.tap("dialog-action-create-character", scroll=True),
                call.wait("Continue building", timeout=90),
                call.wait("home-open-file", timeout=90),
                call.tap("home-open-file"),
                call.wait("FullEditingE2E", timeout=90),
                call.wait("tablet-build-layout", timeout=90),
            ]
        )
        self.assertIsNone(result)
        device.capture.assert_not_called()
        self.assertNotIn(
            call.tap_single_exact_resource_id(
                "phone-destination-runners",
                timeout=45,
                evidence_prefix="phone-destination-runners-tap",
                surface_name="Phone shell destination",
            ),
            device.mock_calls,
        )
        select_document.assert_called_once_with(
            device,
            "career-full-editing-e2e.chum5",
        )

    def test_generic_driver_keeps_the_deferred_tablet_profile(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        self.assertIn('choices=("phone", "tablet")', source)

    def test_full_receipt_binds_completed_fixture_and_exact_restart_identity(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        receipt = source[source.rindex("    receipt = {") :]

        for marker in (
            '"inputFixture": str(args.full_editing_runner.resolve())',
            '"inputFixtureSha256": full_editing_runner_sha256',
            '"verifiedRemoteInputFixtureSha256": verified_remote_sha256[',
            '"importAuthority": optional_workspace_authority_json(imported_authority)',
            '"preRestartAuthority": optional_workspace_authority_json(persisted_authority)',
            '"postRestartAuthority": optional_workspace_authority_json(restored_authority)',
            '"frozenFixtureSha256": full_editing_runner_sha256',
            '"initialLaunchProcessIds": list(initial_launch_state.process_ids)',
            '"initialLaunchResumedComponent": initial_launch_state.resumed_component',
            '"preForceStopProcessIds": list(restart_proof.before_force_stop.process_ids)',
            '"preForceStopResumedComponent": restart_proof.before_force_stop.resumed_component',
            '"postForceStopProcessIds": list(restart_proof.after_force_stop.process_ids)',
            '"restartProcessIds": list(restart_proof.restarted.process_ids)',
            '"restartResumedComponent": restart_proof.restarted.resumed_component',
            '"newRunnerCreationCompletion": "not-claimed"',
            '"newRunnerCreationDraftSaved": (',
            '"pass" if args.profile == "phone" else "not-claimed-tablet-deferred"',
            '"phoneCreationWizardDashboard": (',
            '"pass" if args.profile == "phone" else "not-applicable-tablet-deferred"',
            '"careerRunnerImport": "pass"',
            '"careerRunnerAliasActivated": "FullEditingE2E"',
            '"careerAttributeImprovePersisted": "pass"',
            '"careerAttributeTransition": {',
            '"initialTotal": full_editing_contract.initial_body_total',
            '"improvedTotal": full_editing_contract.improved_body_total',
            '"improvementCost": full_editing_contract.improvement_cost',
            '"remainingKarma": full_editing_contract.remaining_karma',
            '"processRestartPersistence": "pass"',
        ):
            self.assertIn(marker, receipt)

    def test_post_restart_attribute_section_returns_to_build_before_gear(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        marker = (
            "assert_body_total(device, args.profile, "
            "full_editing_contract.improved_body_total)"
        )
        post_restart = source[source.rindex(marker) :]
        route = post_restart[: post_restart.index("open_gear_section(device, args.profile)")]
        self.assertIn('if args.profile == "phone":\n        device.back()', route)

    def test_full_editing_fixture_has_valid_career_body_improvement(self) -> None:
        fixture = REPO_ROOT / "tests" / "fixtures" / "career-full-editing-e2e.chum5"
        self.assertEqual(
            DRIVER.FullEditingFixtureContract(
                initial_body_total=1,
                improved_body_total=2,
                improvement_cost=10,
                initial_karma=35,
                remaining_karma=25,
                next_improvement_cost=15,
            ),
            DRIVER.validate_full_editing_fixture(fixture),
        )

    def test_sparse_condition_fixture_is_rejected_for_full_editing(self) -> None:
        sparse_fixture = (
            REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
        )
        with self.assertRaises(RuntimeError):
            DRIVER.validate_full_editing_fixture(sparse_fixture)

    def test_full_fixture_is_validated_before_transport_or_app_install(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        validation = source.index(
            "validate_full_editing_fixture(args.full_editing_runner.resolve())"
        )
        fixture_transport = source.index("    fixture_inputs = (")
        transport_preflight = source.index("device.require_transport_stability(")
        app_install = source.index("device.install_verified(")
        self.assertLess(validation, fixture_transport)
        self.assertLess(fixture_transport, transport_preflight)
        self.assertLess(fixture_transport, app_install)
        self.assertLess(transport_preflight, app_install)

        generator = (
            REPO_ROOT / "scripts" / "materialize_chummer5_editability_inventory.py"
        ).read_text(encoding="utf-8")
        self.assertIn(
            'REPO_ROOT / "tests" / "fixtures" / "career-full-editing-e2e.chum5"',
            generator,
        )

    def test_exact_career_attribute_text_rejects_prefix_only_match(self) -> None:
        device = Mock()
        device.wait.return_value = DRIVER.UiNode(
            {"text": "Improve · 10 Karma unexpected"}
        )
        with self.assertRaisesRegex(RuntimeError, "Expected exact career attribute text"):
            DRIVER.wait_exact_text(device, "Improve · 10 Karma", timeout=45)
        device.capture.assert_called_once_with("career-attribute-text-mismatch")

    def test_local_import_does_not_claim_success_for_a_guarded_workspace(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        block = source[source.index("public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync") :]
        block = block[
            : block.index(
                "public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync"
            )
        ]

        self.assertIn("CharacterOverviewState previousState = State;", block)
        self.assertIn("_notice = null;", block)
        self.assertIn("ComputeExactImportPayloadSha256(document.Content)", block)
        self.assertIn(
            "if (ActivatedNewWorkspace(previousState, State)",
            block,
        )
        guarded = block[block.index("if (ActivatedNewWorkspace") :]
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", guarded)
        self.assertIn("if (authority is not null)", guarded)
        self.assertIn("RememberRosterLocator", guarded)
        self.assertIn('_notice = $"Opened {document.DisplayName}.";', guarded)
        self.assertNotIn(
            'await _presenter.ImportAsync(\n'
            '                WorkspaceImportDocument.FromUtf8Bytes(document.Content, string.Empty, WorkspaceDocumentFormat.NativeXml),\n'
            '                cancellationToken);\n'
            '            RememberRosterLocator',
            block,
        )

        predicate = block[block.index("private static bool ActivatedNewWorkspace") :]
        self.assertIn("current.WorkspaceId is { } currentWorkspace", predicate)
        self.assertIn("!string.Equals(previousWorkspace.Value, currentWorkspace.Value", predicate)
        self.assertNotIn("ContentRevision", predicate)
        self.assertNotIn("SavedRevision", predicate)

    def test_authoritative_import_and_restart_contracts_fail_closed(self) -> None:
        imported = DRIVER.WorkspaceAuthority("new", 4, 4, "a" * 64, "b" * 64)
        DRIVER.require_import_authority(imported, "a" * 64, "old")
        DRIVER.require_saved_authority(imported)
        DRIVER.require_restored_authority(imported, imported)

        with self.assertRaisesRegex(RuntimeError, "exact verified fixture bytes"):
            DRIVER.require_import_authority(imported, "c" * 64, "old")
        with self.assertRaisesRegex(RuntimeError, "new target workspace"):
            DRIVER.require_import_authority(imported, "a" * 64, "new")
        with self.assertRaisesRegex(RuntimeError, "durably checkpointed"):
            DRIVER.require_saved_authority(imported.__class__("new", 5, 4, "a" * 64, "b" * 64))
        with self.assertRaisesRegex(RuntimeError, "does not match"):
            DRIVER.require_restored_authority(
                imported,
                imported.__class__("new", 4, 4, "a" * 64, "c" * 64),
            )

    def test_workspace_authority_surface_requires_two_identical_reads(self) -> None:
        authority = DRIVER.WorkspaceAuthority("new", 4, 4, "a" * 64, "b" * 64)
        device = Mock()
        with patch.object(
            DRIVER,
            "_read_workspace_authority_once",
            side_effect=[authority, authority],
        ), patch.object(DRIVER, "reset_scroll_to_top") as reset:
            self.assertEqual(authority, DRIVER.read_workspace_authority(device))
        self.assertEqual(2, reset.call_count)

        changed = authority.__class__("new", 5, 5, "c" * 64, "d" * 64)
        with patch.object(
            DRIVER,
            "_read_workspace_authority_once",
            side_effect=[authority, changed],
        ), patch.object(DRIVER, "reset_scroll_to_top"):
            with self.assertRaisesRegex(RuntimeError, "changed during verification"):
                DRIVER.read_workspace_authority(device)
        device.capture.assert_called_with("workspace-authority-surface-changed")

    def test_workspace_authority_accessibility_requires_exact_resource_and_cardinality(self) -> None:
        selector = DRIVER.WORKSPACE_AUTHORITY_RESOURCE_IDS[0]
        exact = DRIVER.UiNode(
            {
                "resource-id": f"com.myexternalbrain.chummer:id/{selector}",
                "text": "workspace-1",
            }
        )
        prefix_lookalike = DRIVER.UiNode(
            {
                "resource-id": f"com.myexternalbrain.chummer:id/{selector}-lookalike",
                "text": "workspace-2",
            }
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = DRIVER.Device(
                Path("/unused/adb"),
                "emulator-5554",
                Path(temporary),
            )
            device.hierarchy = Mock(return_value=[prefix_lookalike, exact])
            device.capture = Mock()
            device.dismiss_system_ui_anr = Mock(return_value=False)

            self.assertIs(
                exact,
                device.wait_for_single_exact_resource_id(selector, timeout=1),
            )
            device.capture.assert_not_called()

            device.hierarchy = Mock(return_value=[exact, exact])
            with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
                device.wait_for_single_exact_resource_id(selector, timeout=1)
            device.capture.assert_called_once_with(
                "workspace-authority-cardinality-invalid"
            )

    def test_exact_accessibility_value_rejects_prefix_and_duplicate_toolbar_nodes(self) -> None:
        exact = DRIVER.UiNode(
            {
                "resource-id": "",
                "content-desc": "build-save-runner",
            }
        )
        prefix_lookalike = DRIVER.UiNode(
            {
                "resource-id": "",
                "content-desc": "build-save-runner-lookalike",
            }
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = DRIVER.Device(
                Path("/unused/adb"),
                "emulator-5554",
                Path(temporary),
            )
            device.hierarchy = Mock(return_value=[prefix_lookalike, exact])
            device.capture = Mock()
            device.dismiss_system_ui_anr = Mock(return_value=False)

            self.assertIs(
                exact,
                device.wait_for_single_exact_accessibility_value(
                    "build-save-runner",
                    timeout=1,
                    evidence_prefix="creation-dashboard-toolbar",
                    surface_name="Creation dashboard toolbar accessibility node",
                ),
            )
            device.capture.assert_not_called()

            device.hierarchy = Mock(return_value=[exact, exact])
            with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
                device.wait_for_single_exact_accessibility_value(
                    "build-save-runner",
                    timeout=1,
                    evidence_prefix="creation-dashboard-toolbar",
                    surface_name="Creation dashboard toolbar accessibility node",
                )
            device.capture.assert_called_once_with(
                "creation-dashboard-toolbar-cardinality-invalid"
            )

    def test_open_creation_dashboard_resets_pruned_viewport_before_exact_binding(self) -> None:
        device = Mock(spec=DRIVER.Device)
        dashboard = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/creation-wizard-dashboard"
                ),
            }
        )
        events: list[str] = []
        viewport = {"reset": False}

        def bind_toolbar(*_args, **_kwargs):
            events.append("toolbar")
            self.assertFalse(viewport["reset"])
            return DRIVER.UiNode({"content-desc": "build-save-runner"})

        def reset(_device, *, swipes: int) -> None:
            self.assertIs(device, _device)
            self.assertEqual(48, swipes)
            events.append("reset")
            viewport["reset"] = True

        def bind_dashboard(*_args, **_kwargs):
            events.append("dashboard")
            self.assertTrue(
                viewport["reset"],
                "The offscreen/pruned dashboard was queried before viewport reset",
            )
            return dashboard

        device.wait_for_single_exact_accessibility_value.side_effect = bind_toolbar
        device.wait_for_single_exact_resource_id.side_effect = bind_dashboard
        with patch.object(
            DRIVER,
            "open_build",
            side_effect=lambda *_args: events.append("open-build"),
        ), patch.object(DRIVER, "reset_scroll_to_top", side_effect=reset):
            actual = DRIVER.open_creation_dashboard(device)

        self.assertIs(dashboard, actual)
        self.assertEqual(["open-build", "toolbar", "reset", "dashboard"], events)
        device.wait_for_single_exact_accessibility_value.assert_called_once_with(
            "build-save-runner",
            timeout=90,
            evidence_prefix="creation-dashboard-toolbar",
            surface_name="Creation dashboard toolbar accessibility node",
        )
        device.wait_for_single_exact_resource_id.assert_called_once_with(
            "creation-wizard-dashboard",
            timeout=90,
            evidence_prefix="creation-dashboard",
            surface_name="Creation dashboard resource node",
        )

    def test_creation_dashboard_exact_resource_cardinality_fails_closed(self) -> None:
        dashboard = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/creation-wizard-dashboard"
                ),
            }
        )
        prefix_lookalike = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/creation-wizard-dashboard-lookalike"
                ),
            }
        )
        with tempfile.TemporaryDirectory() as temporary:
            device = DRIVER.Device(
                Path("/unused/adb"),
                "emulator-5554",
                Path(temporary),
            )
            device.capture = Mock()
            device.dismiss_system_ui_anr = Mock(return_value=False)
            device.hierarchy = Mock(return_value=[prefix_lookalike, dashboard])
            self.assertIs(
                dashboard,
                device.wait_for_single_exact_resource_id(
                    "creation-wizard-dashboard",
                    timeout=1,
                    evidence_prefix="creation-dashboard",
                    surface_name="Creation dashboard resource node",
                ),
            )

            device.hierarchy = Mock(return_value=[dashboard, dashboard])
            with self.assertRaisesRegex(RuntimeError, "cardinality 2"):
                device.wait_for_single_exact_resource_id(
                    "creation-wizard-dashboard",
                    timeout=1,
                    evidence_prefix="creation-dashboard",
                    surface_name="Creation dashboard resource node",
                )
            device.capture.assert_called_once_with(
                "creation-dashboard-cardinality-invalid"
            )

    def test_missing_workspace_authority_accessibility_fails_closed(self) -> None:
        selector = DRIVER.WORKSPACE_AUTHORITY_RESOURCE_IDS[0]
        with tempfile.TemporaryDirectory() as temporary:
            device = DRIVER.Device(
                Path("/unused/adb"),
                "emulator-5554",
                Path(temporary),
            )
            device.hierarchy = Mock(return_value=[])
            device.capture = Mock()
            device.dismiss_system_ui_anr = Mock(return_value=False)

            with self.assertRaisesRegex(RuntimeError, "exactly one"):
                device.wait_for_single_exact_resource_id(selector, timeout=0)
            device.capture.assert_called_once_with("workspace-authority-unavailable")

        authority_device = Mock(spec=DRIVER.Device)
        authority_device.wait_for_single_exact_resource_id.side_effect = RuntimeError(
            "authority unavailable"
        )
        with self.assertRaisesRegex(RuntimeError, "authority unavailable"):
            DRIVER._authority_value(authority_device, selector)

        with self.assertRaisesRegex(RuntimeError, "Unknown workspace authority"):
            DRIVER._authority_value(authority_device, f"{selector}-lookalike")

    def test_workspace_authority_is_runtime_opt_in_and_epoch_bound(self) -> None:
        coordinator = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        home = (
            REPO_ROOT / "src" / "Chummer.Android" / "Native" / "HomePage.cs"
        ).read_text(encoding="utf-8")
        activity = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Platforms"
            / "Android"
            / "MainActivity.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("IChummerClient client", coordinator)
        self.assertIn("using Chummer.Presentation;", coordinator)
        self.assertIn("_client = client;", coordinator)
        self.assertIn("await _client.GetWorkspaceAsync(workspaceId, cancellationToken)", coordinator)
        self.assertEqual(2, coordinator.count("await _client.GetWorkspaceAsync(workspaceId, cancellationToken)"))
        self.assertIn("AuthoritySnapshotsMatch(first, verified)", coordinator)
        self.assertIn("authorityEpoch != _workspaceAuthorityEpoch", coordinator)
        self.assertIn("!authority.Matches(State)", coordinator)
        self.assertIn("public static long Generation", coordinator)
        self.assertIn("_workspaceAuthorityOptInGeneration == AndroidE2EAuthority.Generation", coordinator)
        self.assertIn("optInGeneration == AndroidE2EAuthority.Generation", coordinator)
        self.assertIn("ClearWorkspaceAuthority();\n            return null;", coordinator)
        self.assertIn("WriteInt64BigEndian", coordinator)
        self.assertNotIn("WriteInt32BigEndian", coordinator)
        self.assertIn("if (!AndroidE2EAuthority.Enabled", coordinator)
        self.assertIn("public static event EventHandler? Changed;", coordinator)
        self.assertIn("AndroidE2EAuthority.Changed += OnE2EAuthorityChanged;", coordinator)
        self.assertIn("AndroidE2EAuthority.Changed -= OnE2EAuthorityChanged;", coordinator)
        for resource in (
            "_workspaceActivationGate",
            "_initializeGate",
            "_outputGate",
            "_shellSyncGate",
            "_lifetime",
        ):
            self.assertNotIn(f"{resource}.Dispose();", coordinator)
        self.assertIn("turn an otherwise safe shutdown race into ObjectDisposedException", coordinator)
        self.assertIn("#if DEBUG\n    public NativeWorkspaceAuthoritySnapshot?", coordinator)
        self.assertIn("#if DEBUG\n        AddDebugWorkspaceAuthority();", home)
        debug_surface = home[home.index("private void AddDebugWorkspaceAuthority") :]
        debug_surface = debug_surface[: debug_surface.index("private void AddOnlineSection")]
        self.assertIn(
            "if (Coordinator.DebugWorkspaceAuthority is not { } authority)\n"
            "        {\n"
            "            return;",
            debug_surface,
        )
        for resource_id in DRIVER.WORKSPACE_AUTHORITY_RESOURCE_IDS:
            self.assertEqual(
                1,
                debug_surface.count(f'"{resource_id}"'),
                resource_id,
            )
        self.assertNotIn("ContentUri", debug_surface)
        self.assertNotIn("document.Content", debug_surface)
        self.assertNotIn(".Alias", debug_surface)
        self.assertIn("#if DEBUG", activity)
        self.assertIn(DRIVER.E2E_AUTHORITY_EXTRA, activity)
        self.assertIn("GetBooleanExtra(E2EAuthorityIntentExtra, false)", activity)
        self.assertNotIn("GetStringExtra(E2EAuthorityIntentExtra)", activity)

    def test_proof_refresh_failure_cannot_create_a_false_open_notice_or_locator(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        local = source[source.index("public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync") :]
        local = local[
            : local.index(
                "public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync"
            )
        ]
        save = source[source.index("public async Task SaveAsync") :]
        save = save[: save.index("public async Task ExportAsync")]
        refresh = source[source.index("private async Task<NativeWorkspaceAuthoritySnapshot?>") :]
        refresh = refresh[: refresh.index("private void ClearWorkspaceAuthority")]

        self.assertLess(local.index("await _presenter.ImportAsync"), local.index("TryRefreshWorkspaceAuthorityAsync"))
        guarded = local[local.index("if (authority is not null)") :]
        self.assertLess(guarded.index("if (authority is not null)"), guarded.index("RememberRosterLocator"))
        self.assertLess(guarded.index("if (authority is not null)"), guarded.index('_notice = $"Opened'))
        self.assertIn("else\n                {\n                    _notice = WorkspaceVerificationUnavailableNotice;", guarded)
        self.assertIn("public string? Notice => _notice ?? State.Notice", source)
        self.assertLess(save.index("await _presenter.SaveAsync"), save.index("TryRefreshWorkspaceAuthorityAsync"))
        self.assertIn("catch (Exception exception) when (exception is not OutOfMemoryException)", refresh)
        self.assertIn("ClearWorkspaceAuthority();", refresh)
        self.assertIn("return null;", refresh)

    def test_import_receipts_require_verified_authority_after_final_state_restore(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        local = source[
            source.index("public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync") :
            source.index("private static bool ActivatedNewWorkspace")
        ]
        online = source[
            source.index("public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync") :
            source.index("public async Task CreateRunnerAsync")
        ]

        for name, block in (("local", local), ("online", online)):
            with self.subTest(name=name):
                authority_guard = block[block.index("if (authority is not null)") :]
                receipt_assignment = block.index("activation = new(")
                self.assertIn("activatedWorkspaceId = importedWorkspaceId;", authority_guard)
                self.assertIn("verifiedAuthority = authority;", authority_guard)
                self.assertLess(block.index("await SyncShellAsync"), receipt_assignment)
                self.assertLess(block.index("RestorePlayState();"), receipt_assignment)
                self.assertIn(
                    "verifiedAuthority?.Matches(State) == true",
                    block[block.index("RestorePlayState();") : receipt_assignment],
                )
                self.assertIn(
                    "WorkspaceIsActive(State, stableWorkspaceId)",
                    block[block.index("RestorePlayState();") : receipt_assignment],
                )
                self.assertNotIn("catch (", block)

        predicate = source[source.index("private static bool WorkspaceIsActive") :]
        predicate = predicate[: predicate.index("public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync")]
        self.assertIn("state.WorkspaceId is { } activeWorkspaceId", predicate)
        self.assertIn("StringComparison.Ordinal", predicate)

    def test_workspace_switch_receipt_binds_requested_identity_and_optional_proof(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        block = source[
            source.index("public async Task<NativeWorkspaceActivationReceipt?> SwitchWorkspaceAsync") :
            source.index("public async Task CloseWorkspaceAsync")
        ]

        self.assertIn("bool authorityRequired = AndroidE2EAuthority.Enabled;", block)
        self.assertIn("expectedWorkspaceId: workspace.Id", block)
        self.assertLess(block.index("_presenter.SwitchWorkspaceAsync"), block.index("SyncShellAsync"))
        self.assertLess(block.index("SyncShellAsync"), block.index("TryRefreshWorkspaceAuthorityAsync"))
        self.assertLess(block.index("TryRefreshWorkspaceAuthorityAsync"), block.index("RestorePlayState();"))
        self.assertLess(
            block.index("RestorePlayState();"),
            block.index("bool authorityRequired = AndroidE2EAuthority.Enabled;"),
        )
        self.assertLess(
            block.index("bool authorityRequired = AndroidE2EAuthority.Enabled;"),
            block.index("WorkspaceIsActive(State, workspace.Id)"),
        )
        self.assertIn("(!authorityRequired || authority?.Matches(State) == true)", block)
        self.assertIn("NativeWorkspaceActivationKind.WorkspaceSwitch", block)
        self.assertIn("workspace.Id)", block)

    def test_online_import_uses_the_same_guarded_activation_contract(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        block = source[
            source.index(
                "public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync"
            ) :
        ]
        block = block[: block.index("public async Task CreateRunnerAsync")]

        self.assertIn("CharacterOverviewState previousState = State;", block)
        self.assertIn("_notice = null;", block)
        self.assertIn("await _workspaceActivationGate.WaitAsync(cancellationToken);", block)
        self.assertIn("string expectedPayloadSha256 = Sha256Hex(payload);", block)
        self.assertIn(
            "if (ActivatedNewWorkspace(previousState, State)",
            block,
        )
        guarded = block[block.index("if (ActivatedNewWorkspace") :]
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", guarded)
        self.assertIn("if (authority is not null)", guarded)
        self.assertIn("RememberRosterLocator", guarded)
        self.assertIn(
            '_notice = $"Opened {DisplayName(character.Name, character.Alias)}.";',
            guarded,
        )
        self.assertIn(
            "else\n                {\n                    _notice = WorkspaceVerificationUnavailableNotice;",
            guarded,
        )
        self.assertEqual(2, source.count("_notice = WorkspaceVerificationUnavailableNotice;"))
        self.assertNotIn(
            'cancellationToken);\n'
            '            RememberRosterLocator(',
            block,
        )

    def test_import_buffers_are_allocated_only_after_the_shared_activation_gate(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        local = source[source.index("public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync") :]
        local = local[: local.index("private static bool ActivatedNewWorkspace")]
        online = source[
            source.index(
                "public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync"
            ) :
        ]
        online = online[: online.index("public async Task CreateRunnerAsync")]

        self.assertLess(local.index("_workspaceActivationGate.WaitAsync"), local.index("_documents.OpenAsync"))
        self.assertLess(local.index("_documents.OpenAsync"), local.index("ComputeExactImportPayloadSha256"))
        self.assertLess(local.index("CryptographicOperations.ZeroMemory"), local.index("_workspaceActivationGate.Release"))
        self.assertLess(online.index("_workspaceActivationGate.WaitAsync"), online.index("StrictUtf8.GetBytes"))
        self.assertLess(online.index("StrictUtf8.GetBytes"), online.index("Sha256Hex(payload)"))
        self.assertLess(online.index("CryptographicOperations.ZeroMemory"), online.index("_workspaceActivationGate.Release"))

    def test_explicit_android_activation_entrypoints_share_one_gate(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")

        for method_name, start_marker, next_method in (
            ("ConfirmCreationPrerequisiteAsync", "ConfirmCreationPrerequisiteAsync(", "ConfirmCreationPrerequisiteCoreAsync"),
            ("ConfirmCreationFoundationAsync", "public async Task<CharacterCreationFoundationInteractionConfirmResult> ConfirmCreationFoundationAsync(", "ConfirmCreationFoundationCoreAsync"),
            (
                "SwitchWorkspaceAsync",
                "public async Task<NativeWorkspaceActivationReceipt?> SwitchWorkspaceAsync",
                "CloseWorkspaceAsync",
            ),
            ("CloseWorkspaceAsync", "public async Task CloseWorkspaceAsync", "private async Task WithWorkspaceActivationGateAsync"),
            ("EraseAccountAsync", "public async Task<NativeAccountErasureResult> EraseAccountAsync", "EraseAccountCoreAsync"),
            ("ExecuteCommandAsync", "public async Task ExecuteCommandAsync", "ExecuteCommandCoreAsync"),
            ("ExecuteDialogActionAsync", "public async Task ExecuteDialogActionAsync", "ExecuteDialogActionCoreAsync"),
            ("ExecuteWorkspaceActionAsync", "public async Task ExecuteWorkspaceActionAsync", "ExecuteWorkspaceActionCoreAsync"),
            ("ApplyAttributeEditAsync", "public async Task ApplyAttributeEditAsync", "ApplyAttributeEditCoreAsync"),
            ("ApplyOriginDossierEditAsync", "public async Task ApplyOriginDossierEditAsync", "ApplyOriginDossierEditCoreAsync"),
            ("ApplyCollectionMutationAsync", "public async Task ApplyCollectionMutationAsync", "ApplyCollectionMutationCoreAsync"),
            ("ApplyConditionMonitorEditAsync", "public async Task ApplyConditionMonitorEditAsync", "ApplyConditionMonitorEditCoreAsync"),
            ("ApplyPrimaryArmEditAsync", "public async Task ApplyPrimaryArmEditAsync", "ApplyPrimaryArmEditCoreAsync"),
        ):
            block = source[source.index(start_marker) :]
            block = block[: block.index(next_method, len(method_name))]
            self.assertIn("WithWorkspaceActivationGateAsync", block, method_name)

        create = source[source.index("public async Task CreateRunnerAsync") :]
        create = create[
            : create.index(
                "public async Task<NativeWorkspaceActivationReceipt?> SwitchWorkspaceAsync"
            )
        ]
        self.assertIn('ExecuteCommandAsync("new_character", cancellationToken)', create)
        self.assertNotIn("WithWorkspaceActivationGateAsync", create)

        initialize = source[source.index("public async Task InitializeAsync") :]
        initialize = initialize[
            : initialize.index(
                "public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync"
            )
        ]
        self.assertLess(
            initialize.index("_workspaceActivationGate.WaitAsync"),
            initialize.index("_presenter.InitializeAsync"),
        )
        helper = source[source.index("private async Task WithWorkspaceActivationGateAsync") :]
        helper = helper[: helper.index("public bool IsRosterFavorite")]
        self.assertIn("finally", helper)
        self.assertIn("_workspaceActivationGate.Release();", helper)

    def test_authority_capture_uses_the_shared_presentation_operation_coordinator(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        maui = (
            REPO_ROOT / "src" / "Chummer.Android" / "MauiProgram.cs"
        ).read_text(encoding="utf-8")
        refresh = source[source.index("private async Task<NativeWorkspaceAuthoritySnapshot?>") :]
        refresh = refresh[: refresh.index("private void ClearWorkspaceAuthority")]

        registration = (
            "builder.Services.AddSingleton<IWorkspaceOperationCoordinator, "
            "WorkspaceOperationCoordinator>();"
        )
        self.assertIn(registration, maui)
        self.assertLess(
            maui.index(registration),
            maui.index("AddSingleton<ICharacterOverviewPresenter, CharacterOverviewPresenter>()"),
        )
        self.assertLess(
            maui.index(registration),
            maui.index("AddSingleton<RunnerSessionCoordinator>()"),
        )
        self.assertIn("IWorkspaceOperationCoordinator workspaceOperationCoordinator", source)
        self.assertIn("_workspaceOperationCoordinator = workspaceOperationCoordinator;", source)
        self.assertIn("_workspaceOperationCoordinator.RunCurrentAsync(", refresh)
        run_current = refresh[refresh.index("_workspaceOperationCoordinator.RunCurrentAsync(") :]
        self.assertLess(run_current.index("ReadWorkspaceAuthorityAsync"), run_current.index("RequirePayloadDigest"))
        self.assertLess(run_current.index("RequirePayloadDigest"), run_current.index("linked.Token"))
        self.assertIn("|| !execution.HasValue", run_current)
        self.assertIn("|| execution.Value is not { } authority", run_current)
        self.assertLess(run_current.index("!execution.CanPublish"), run_current.index("execution.Value"))
        self.assertIn("ClearWorkspaceAuthority();\n                return null;", run_current)

        for method_name, start_marker, presenter_operation in (
            ("InitializeAsync", "public async Task InitializeAsync", "_presenter.InitializeAsync"),
            (
                "OpenLocalAsync",
                "public async Task<NativeWorkspaceActivationReceipt?> OpenLocalAsync",
                "_presenter.ImportAsync",
            ),
            (
                "OpenOnlineAsync",
                "public async Task<NativeWorkspaceActivationReceipt?> OpenOnlineAsync",
                "_presenter.ImportAsync",
            ),
            (
                "SwitchWorkspaceAsync",
                "public async Task<NativeWorkspaceActivationReceipt?> SwitchWorkspaceAsync",
                "_presenter.SwitchWorkspaceAsync",
            ),
            ("CloseWorkspaceAsync", "public async Task CloseWorkspaceAsync", "_presenter.CloseWorkspaceAsync"),
            ("SaveAsync", "public async Task SaveAsync", "_presenter.SaveAsync"),
        ):
            block = source[source.index(start_marker) :]
            next_public = block.find("\n    public ", 10)
            if next_public >= 0:
                block = block[:next_public]
            self.assertLess(
                block.index(presenter_operation),
                block.index("TryRefreshWorkspaceAuthorityAsync"),
                method_name,
            )

    def test_opt_in_changes_clear_then_rerender_and_refresh_without_static_leaks(self) -> None:
        source = (
            REPO_ROOT
            / "src"
            / "Chummer.Android"
            / "Native"
            / "RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        handler = source[source.index("private void OnE2EAuthorityChanged") :]
        handler = handler[: handler.index("private void NotifyChanged")]

        self.assertLess(handler.index("ClearWorkspaceAuthority();"), handler.index("NotifyChanged();"))
        self.assertIn("if (AndroidE2EAuthority.Enabled && !_disposed)", handler)
        self.assertIn("RefreshWorkspaceAuthorityForOptInAsync", handler)
        self.assertIn("expectedWorkspaceId: State.WorkspaceId", handler)
        self.assertIn("_lifetime.Token", handler)
        self.assertIn("catch (Exception exception) when (exception is not OutOfMemoryException)", handler)

    def test_native_dialog_rebuilds_from_state_shape_changes_not_a_dialog_allowlist(self) -> None:
        source = (
            REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
        ).read_text(encoding="utf-8")
        start = source.index("Task UpdateFieldAsync")
        end = source.index("private async Task ExecuteAsync", start)
        block = source[start:end]

        self.assertIn("DesktopDialogState? previous", block)
        self.assertIn("RequiresStructuralRerender(previous, next, binding.FieldId)", block)
        self.assertIn("FieldShapeMatches", block)
        self.assertIn("OptionsMatch", block)
        self.assertNotIn("dialog.new_character.priority_workflow", block)
        self.assertNotIn("dialog.new_character.karma_workflow", block)

    def test_collection_openers_use_overlapping_search_below_the_action_list(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, action, quick_add in (
            ("open_contact_section", "build-action-tab-relationships-contacts", "section-quick-contact-add"),
            ("open_pet_section", "build-action-tab-relationships-pets", "section-quick-contact-add"),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn("_open_phone_relationship_collection(", block)
            self.assertIn(f'"{action}"', block)
            self.assertIn(f'"{quick_add}"', block)

        helper = source[source.index("def _open_phone_relationship_collection") :]
        helper = helper[: helper.index("\ndef ", 5)]
        self.assertEqual(2, helper.count("device.tap_exact_resource_id_bidirectional("))
        self.assertNotIn("device.tap_bidirectional(", helper)
        self.assertNotIn("exact_resource_id=True", helper)
        self.assertIn('"build-section-tab-relationships"', helper)
        self.assertIn("forward_scrolls=48", helper)
        self.assertIn("device.wait_exact_resource_id_bidirectional(", helper)
        self.assertIn("reset_scroll_to_top(device, swipes=24)", helper)
        self.assertIn("max_scrolls=24", helper)
        empty_marker = "No entries yet. Use an action above to add one."
        self.assertIn(empty_marker, helper)
        self.assertLess(
            helper.index(empty_marker),
            helper.index("device.wait_exact_resource_id_bidirectional("),
        )

    def test_relationship_openers_wait_for_fixture_items_instead_of_quick_add(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, phone_action, tablet_action in (
            (
                "open_contact_section",
                "build-action-tab-relationships-contacts",
                "tablet-build-action-tab-relationships-contacts",
            ),
            (
                "open_pet_section",
                "build-action-tab-relationships-pets",
                "tablet-build-action-tab-relationships-pets",
            ),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn("expected_item: str | None = None", block)
            self.assertIn("_open_phone_relationship_collection(", block)
            self.assertIn(f'"{phone_action}"', block)
            self.assertIn(f'"{tablet_action}"', block)
            self.assertIn("expected_item=expected_item", block)
            self.assertNotIn("device.swipe_up(", block)
            self.assertIn("max_scrolls=8", block)
            self.assertIn("time.sleep(2)", block)
            self.assertIn("time.sleep(5)", block)
            self.assertIn("timeout=180", block)

    def test_tablet_section_openers_fully_reset_the_long_build_rail(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name in (
            "open_attribute_section",
            "open_gear_section",
            "open_contact_section",
            "open_pet_section",
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertIn(
                "reset_scroll_to_top(device, x_ratio=0.15, swipes=24)",
                block,
            )

    def test_phone_gear_save_resets_the_editor_before_persistence_assertion(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        phone_save = source[source.index('device.set_text("collection-field-customname"') :]
        phone_save = phone_save[: phone_save.index("device.back()")]

        self.assertIn("reset_scroll_to_top(device, swipes=6)", phone_save)
        self.assertLess(
            phone_save.index("reset_scroll_to_top(device, swipes=6)"),
            phone_save.index('device.assert_text("GearProofE2E")'),
        )

    def test_contact_and_pet_editors_reset_before_field_mutations(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name in (
            "add_and_edit_contact",
            "assert_contact_persisted",
            "add_and_edit_pet",
            "assert_pet_persisted",
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            self.assertLess(
                block.index("tap_collection_item(device,"),
                block.index("reset_collection_editor_to_top(device, profile)"),
            )

    def test_contact_journey_uses_fixture_bounds_and_saves_ratings_before_toggles(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        block = source[source.index("def add_and_edit_contact") :]
        block = block[: block.index("\ndef ", 5)]
        self.assertIn("connection_maximum: int = 6", block)
        self.assertIn('f"Connection · 1–{connection_maximum}",', block)
        self.assertIn("str(connection_maximum + 1)", block)
        self.assertIn("str(connection_maximum)", block)
        ratings_save = block.index("device.tap(save, scroll=True)")
        toggle_batch = block.index("for toggle in editable_toggles")
        self.assertIn('editable_toggles = ("group", "family", "blackmail")', block)
        self.assertIn('capture=f"{profile}-career-contact-free-authority-invalid"', block)
        self.assertLess(
            ratings_save,
            toggle_batch,
        )

    def test_contact_connection_bound_matches_creation_and_career_semantics(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_pet = source[source.index('if args.journey == "contact-pet":') :]
        contact_pet = contact_pet[: contact_pet.index('if args.journey == "condition-monitor":')]
        full_journey = source[source.index("    add_and_edit_gear(device, args.profile)") :]

        self.assertIn(
            "add_and_edit_contact(device, args.profile, create_items=False)",
            contact_pet,
        )
        self.assertNotIn("connection_maximum=12", contact_pet)
        self.assertIn("assert_contact_persisted(device, args.profile)", contact_pet)
        self.assertIn("connection_maximum=12", full_journey)
        self.assertIn("free_editable=False", full_journey)
        self.assertNotIn("free_editable=False", contact_pet)

    def test_contact_free_proof_follows_creation_and_career_authority(self) -> None:
        for free_editable, expected_enabled in (
            (True, True),
            (False, False),
        ):
            with self.subTest(free_editable=free_editable):
                device = Mock(spec=DRIVER.Device)
                device.find.return_value = None
                device.wait.return_value = DRIVER.UiNode({"checked": "false", "enabled": "true"})
                with (
                    patch.object(DRIVER, "open_contact_section"),
                    patch.object(DRIVER, "tap_collection_item"),
                    patch.object(DRIVER, "reset_collection_editor_to_top"),
                    patch.object(DRIVER, "reset_scroll_to_top"),
                    patch.object(DRIVER, "ensure_checked") as ensure_checked,
                    patch.object(DRIVER, "assert_toggle_state") as assert_toggle_state,
                    patch.object(DRIVER.time, "sleep"),
                ):
                    DRIVER.add_and_edit_contact(
                        device,
                        "phone",
                        create_items=False,
                        connection_maximum=12 if not free_editable else 6,
                        free_editable=free_editable,
                    )

                self.assertEqual(
                    [
                        "collection-toggle-group",
                        "collection-toggle-family",
                        "collection-toggle-blackmail",
                    ],
                    [call.args[1] for call in ensure_checked.call_args_list],
                )
                self.assertEqual(1, assert_toggle_state.call_count)
                self.assertEqual(
                    "collection-toggle-free",
                    assert_toggle_state.call_args.args[1],
                )
                self.assertFalse(assert_toggle_state.call_args.kwargs["checked"])
                self.assertEqual(expected_enabled, assert_toggle_state.call_args.kwargs["enabled"])

    def test_assert_toggle_state_rejects_disabled_or_wrong_checked_authority(self) -> None:
        device = Mock(spec=DRIVER.Device)
        device.wait.return_value = DRIVER.UiNode({"checked": "false", "enabled": "false"})
        DRIVER.assert_toggle_state(
            device,
            "collection-toggle-free",
            checked=False,
            enabled=False,
        )

        with self.assertRaisesRegex(RuntimeError, "enabled=True"):
            DRIVER.assert_toggle_state(
                device,
                "collection-toggle-free",
                checked=False,
                enabled=True,
                capture="free-disabled",
            )
        device.capture.assert_called_with("free-disabled")

        with self.assertRaisesRegex(RuntimeError, "checked=True"):
            DRIVER.assert_toggle_state(
                device,
                "collection-toggle-free",
                checked=True,
                capture="free-not-checked",
            )
        device.capture.assert_called_with("free-not-checked")

        for malformed in (
            {"enabled": "false"},
            {"checked": "false"},
            {"checked": "False", "enabled": "false"},
            {"checked": "false", "enabled": "False"},
        ):
            with self.subTest(malformed=malformed):
                device.wait.return_value = DRIVER.UiNode(malformed)
                with self.assertRaisesRegex(RuntimeError, "state mismatch"):
                    DRIVER.assert_toggle_state(
                        device,
                        "collection-toggle-free",
                        checked=False,
                        enabled=False,
                        capture="free-malformed",
                    )
                device.capture.assert_called_with("free-malformed")

    def test_creation_free_contact_is_target_isolated_from_group(self) -> None:
        device = Mock(spec=DRIVER.Device)
        with (
            patch.object(DRIVER, "open_contact_section"),
            patch.object(DRIVER, "tap_collection_item"),
            patch.object(DRIVER, "reset_collection_editor_to_top"),
            patch.object(DRIVER, "reset_scroll_to_top"),
            patch.object(DRIVER, "ensure_checked") as ensure_checked,
            patch.object(DRIVER, "assert_toggle_state") as assert_toggle_state,
            patch.object(DRIVER.time, "sleep"),
        ):
            DRIVER.edit_creation_free_contact(device, "phone")

        ensure_checked.assert_called_once_with(device, "collection-toggle-free")
        observed = [
            (call.args[1], call.kwargs["checked"], call.kwargs.get("enabled"))
            for call in assert_toggle_state.call_args_list
        ]
        self.assertEqual(
            [
                ("collection-toggle-group", False, None),
                ("collection-toggle-free", False, True),
                ("collection-toggle-group", False, None),
                ("collection-toggle-free", True, True),
            ],
            observed,
        )

    def test_creation_free_contact_restart_proof_keeps_other_toggles_false(self) -> None:
        device = Mock(spec=DRIVER.Device)
        with (
            patch.object(DRIVER, "open_contact_section"),
            patch.object(DRIVER, "tap_collection_item"),
            patch.object(DRIVER, "reset_collection_editor_to_top"),
            patch.object(DRIVER, "assert_toggle_state") as assert_toggle_state,
        ):
            DRIVER.assert_creation_free_contact_persisted(device, "phone")

        observed = [
            (call.args[1], call.kwargs["checked"], call.kwargs.get("enabled"))
            for call in assert_toggle_state.call_args_list
        ]
        self.assertEqual(
            [
                ("collection-toggle-group", False, None),
                ("collection-toggle-family", False, None),
                ("collection-toggle-blackmail", False, None),
                ("collection-toggle-free", True, True),
            ],
            observed,
        )

    def test_contact_connection_invalid_probe_save_and_readback_use_the_active_runner_bound(self) -> None:
        persisted_fields = [
            "ContactPersistedE2E",
            "ContactNotesE2E",
            "FixerE2E",
            "ViennaE2E",
            "ElfE2E",
            "NonbinaryE2E",
            "42",
            "ProfessionalE2E",
            "CredstickE2E",
            "UrbanExplorerE2E",
            "PrivateE2E",
            "NightMarketE2E",
        ]
        for maximum, invalid in ((6, "7"), (12, "13")):
            with self.subTest(maximum=maximum):
                device = Mock(spec=DRIVER.Device)
                device.find.return_value = None
                device.wait.return_value = DRIVER.UiNode({"checked": "true"})
                with (
                    patch.object(DRIVER, "open_contact_section"),
                    patch.object(DRIVER, "tap_collection_item"),
                    patch.object(DRIVER, "reset_collection_editor_to_top"),
                    patch.object(DRIVER, "ensure_checked"),
                    patch.object(DRIVER, "assert_toggle_state"),
                    patch.object(DRIVER.time, "sleep"),
                ):
                    DRIVER.add_and_edit_contact(
                        device,
                        "phone",
                        create_items=False,
                        connection_maximum=maximum,
                    )

                connection_calls = [
                    observed
                    for observed in device.set_text.call_args_list
                    if observed.args[0] == "collection-contact-connection-"
                ]
                self.assertEqual(2, len(connection_calls))
                self.assertEqual(
                    (
                        "collection-contact-connection-",
                        f"Connection · 1–{maximum}",
                        invalid,
                    ),
                    connection_calls[0].args,
                )
                self.assertEqual(str(maximum), connection_calls[1].args[2])

                with (
                    patch.object(DRIVER, "open_contact_section"),
                    patch.object(DRIVER, "tap_collection_item"),
                    patch.object(DRIVER, "reset_collection_editor_to_top"),
                    patch.object(DRIVER, "assert_toggle_state"),
                    patch.object(
                        DRIVER,
                        "selected_text",
                        side_effect=[*persisted_fields, str(maximum)],
                    ) as selected_text,
                ):
                    DRIVER.assert_contact_persisted(
                        device,
                        "phone",
                        connection_maximum=maximum,
                    )

                self.assertEqual(
                    f"Connection · 1–{maximum}",
                    selected_text.call_args_list[-1].args[2],
                )

                if maximum == 12:
                    with (
                        patch.object(DRIVER, "open_contact_section"),
                        patch.object(DRIVER, "tap_collection_item"),
                        patch.object(DRIVER, "reset_collection_editor_to_top"),
                        patch.object(DRIVER, "assert_toggle_state"),
                        patch.object(
                            DRIVER,
                            "selected_text",
                            side_effect=[*persisted_fields, "6"],
                        ),
                    ):
                        with self.assertRaisesRegex(
                            RuntimeError,
                            "expected '12', got '6'",
                        ):
                            DRIVER.assert_contact_persisted(
                                device,
                                "phone",
                                connection_maximum=12,
                            )
                    device.capture.assert_called_with(
                        "phone-contact-connection-12-not-persisted"
                    )

    def test_contact_restart_proof_reads_free_authority_by_mode(self) -> None:
        persisted_fields = [
            "ContactPersistedE2E",
            "ContactNotesE2E",
            "FixerE2E",
            "ViennaE2E",
            "ElfE2E",
            "NonbinaryE2E",
            "42",
            "ProfessionalE2E",
            "CredstickE2E",
            "UrbanExplorerE2E",
            "PrivateE2E",
            "NightMarketE2E",
        ]
        for free_editable, maximum, expected in (
            (True, 6, ("collection-toggle-free", False, True)),
            (False, 12, ("collection-toggle-free", False, False)),
        ):
            with self.subTest(free_editable=free_editable):
                device = Mock(spec=DRIVER.Device)
                device.find.return_value = None
                with (
                    patch.object(DRIVER, "open_contact_section"),
                    patch.object(DRIVER, "tap_collection_item"),
                    patch.object(DRIVER, "reset_collection_editor_to_top"),
                    patch.object(
                        DRIVER,
                        "selected_text",
                        side_effect=[*persisted_fields, str(maximum)],
                    ),
                    patch.object(DRIVER, "assert_toggle_state") as assert_toggle_state,
                ):
                    DRIVER.assert_contact_persisted(
                        device,
                        "phone",
                        connection_maximum=maximum,
                        free_editable=free_editable,
                    )

                free_call = next(
                    call
                    for call in assert_toggle_state.call_args_list
                    if call.args[1] == "collection-toggle-free"
                )
                self.assertEqual(expected[0], free_call.args[1])
                self.assertEqual(expected[1], free_call.kwargs["checked"])
                self.assertEqual(expected[2], free_call.kwargs["enabled"])

    def test_restart_gear_proof_reads_the_persisted_custom_name_field(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        restart = source[source.index('device.shell("am", "force-stop", PACKAGE)') :]
        self.assertIn('tap_collection_item(device, "Ares Predator V")', restart)
        self.assertIn('selected_text(device, gear_field, "Custom Name", scroll=True)', restart)
        self.assertIn('persisted_custom_name != "GearProofE2E"', restart)
        self.assertNotIn('tap_collection_item(device, "GearProofE2E")', restart)

    def test_unlink_restore_uses_the_waited_name_node_without_recapturing(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        unlink = source[source.index("def assert_link_persisted_then_remove") :]
        unlink = unlink[: unlink.index("def add_and_edit_gear")]

        self.assertIn('restored = name_node.attributes.get("text", "")', unlink)
        self.assertNotIn(
            'selected_text(device, name_selector, "Name", scroll=True)',
            unlink,
        )

    def test_contact_dialog_resets_after_locating_its_bottom_action(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_add = source[source.index("def add_contact_from_dialog") :]
        contact_add = contact_add[: contact_add.index("def add_and_edit_contact")]

        action_wait = contact_add.index('"dialog-action-add",')
        reset = contact_add.index("reset_scroll_to_top(device")
        name_edit = contact_add.index('device.set_text("dialog-field-uicontactname"')
        self.assertLess(action_wait, reset)
        self.assertLess(reset, name_edit)
        self.assertIn("max_scrolls=48", contact_add)
        self.assertIn("scroll_distance_ratio=0.28", contact_add)
        self.assertIn("swipes=24", contact_add)

    def test_tablet_gear_editor_resets_inspector_before_custom_name(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        gear_edit = source[source.index("def add_and_edit_gear") :]
        gear_edit = gear_edit[: gear_edit.index("def add_contact_from_dialog")]

        save_wait = gear_edit.index('device.wait("tablet-inspector-save"')
        reset = gear_edit.index("reset_scroll_to_top(device, x_ratio=0.82", save_wait)
        custom_name = gear_edit.index('device.set_text("tablet-field-customname"')
        self.assertLess(save_wait, reset)
        self.assertLess(reset, custom_name)

    def test_tablet_gear_save_reads_back_exact_custom_name_field(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        gear_edit = source[source.index("def add_and_edit_gear") :]
        gear_edit = gear_edit[: gear_edit.index("def add_contact_from_dialog")]

        self.assertIn(
            'selected_text(\n            device,\n            "tablet-field-customname",',
            gear_edit,
        )
        self.assertIn('saved_custom_name != "GearProofE2E"', gear_edit)
        tablet_branch = gear_edit[: gear_edit.index("return")]
        self.assertNotIn('device.assert_text("GearProofE2E")', tablet_branch)

    def test_keyboard_visibility_requires_an_active_ime(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.input_method_output = "mImeWindowVis=0\n      mInputShown=false"
            self.assertFalse(device.keyboard_visible())

            device.input_method_output = "mImeWindowVis=0x3\n      mInputShown=true"
            self.assertTrue(device.keyboard_visible())

    def test_text_entry_uses_escape_to_hide_ime_without_navigating_back(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.input_method_output = "mImeWindowVis=0x3\n      mInputShown=true"
            device.hide_keyboard_on_escape = True
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "resource-id": "app:id/collection-field-name-id",
                        "text": "Old",
                        "clickable": "true",
                        "bounds": "[100,400][900,520]",
                    }
                )
            ]

            device.set_text("collection-field-name", "Name", "New")

        self.assertIn(("input", "keyevent", "111"), device.commands)
        self.assertNotIn(("input", "tap", "162", "2350"), device.commands)
        self.assertNotIn(("input", "keyevent", "4"), device.commands)

    def test_input_nodes_in_the_system_navigation_zone_are_not_tappable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            safe = DRIVER.UiNode({"bounds": "[53,1727][1028,1831]"})
            navigation_zone = DRIVER.UiNode({"bounds": "[53,2190][1028,2400]"})
            collapsed = DRIVER.UiNode({"bounds": "[53,2400][1028,2400]"})

            self.assertTrue(device.input_node_is_tappable(safe))
            self.assertFalse(device.input_node_is_tappable(navigation_zone))
            self.assertFalse(device.input_node_is_tappable(collapsed))

    def test_clipped_or_inverted_action_bounds_are_not_tappable(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 2560x1800")
            safe = DRIVER.UiNode({"bounds": "[88,1500][504,1600]"})
            clipped = DRIVER.UiNode({"bounds": "[88,1695][504,1688]"})
            system_zone = DRIVER.UiNode({"bounds": "[88,1740][504,1800]"})

            self.assertTrue(device.node_has_tappable_bounds(safe))
            self.assertFalse(device.node_has_tappable_bounds(clipped))
            self.assertFalse(device.node_has_tappable_bounds(system_zone))

    def test_empty_text_entry_clears_selection_without_invalid_adb_text_call(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "resource-id": "app:id/collection-field-name-id",
                        "text": "Old",
                        "clickable": "true",
                        "bounds": "[100,400][900,520]",
                    }
                )
            ]

            device.set_text("collection-field-name", "Name", "")

        self.assertIn(("input", "keyevent", "67"), device.commands)
        self.assertNotIn(("input", "text", ""), device.commands)

    def test_dense_contact_edits_use_overlapping_scrolls(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_edit = source[source.index("def add_and_edit_contact") :]
        contact_edit = contact_edit[: contact_edit.index("def assert_contact_persisted")]

        self.assertIn("scroll_distance_ratio=0.22", contact_edit)
        self.assertIn("max_scrolls=20", contact_edit)
        rating_block = contact_edit[contact_edit.index("connection_selector =") :]
        self.assertLess(
            rating_block.index("reset_scroll_to_top("),
            rating_block.index("device.set_text("),
        )

    def test_toggle_search_uses_overlapping_scrolls(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        ensure_checked = source[source.index("def ensure_checked") :]
        ensure_checked = ensure_checked[: ensure_checked.index("def selected_text")]

        self.assertIn("max_scrolls=20", ensure_checked)
        self.assertIn("scroll_distance_ratio=0.22", ensure_checked)
        self.assertIn("device.tap(", ensure_checked)
        self.assertIn('device.capture("toggle-state-failed")', ensure_checked)
        self.assertNotIn('device.shell("input", "tap"', ensure_checked)

    def test_text_field_scrolling_waits_for_android_ui_tree_to_settle(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        set_text = source[source.index("    def set_text(") :]
        set_text = set_text[: set_text.index("    def input_node_is_tappable(")]

        swipe = set_text.index("self.swipe_up(")
        settle = set_text.index("time.sleep(0.75)", swipe)
        self.assertLess(swipe, settle)

    def test_contact_connection_correction_resets_after_validation_dialog(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_edit = source[source.index("def add_and_edit_contact") :]
        contact_edit = contact_edit[: contact_edit.index("def assert_contact_persisted")]
        correction = contact_edit[contact_edit.index('device.tap("OK")') :]

        self.assertLess(
            correction.index("reset_scroll_to_top("),
            correction.index("device.set_text("),
        )

    def test_contact_group_toggle_resets_after_first_successful_save(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        contact_edit = source[source.index("def add_and_edit_contact") :]
        contact_edit = contact_edit[: contact_edit.index("def assert_contact_persisted")]
        first_save = contact_edit.index("device.tap(save, scroll=True)")
        reset = contact_edit.index("reset_scroll_to_top(", first_save)
        toggle_batch = contact_edit.index("for toggle in editable_toggles", reset)

        self.assertLess(first_save, reset)
        self.assertLess(reset, toggle_batch)

    def test_phone_contact_and_pet_wait_for_collection_after_save(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        for function_name, persisted_name in (
            ("add_and_edit_contact", "ContactPersistedE2E"),
            ("add_and_edit_pet", "PetPersistedE2E"),
        ):
            block = source[source.index(f"def {function_name}") :]
            block = block[: block.index("\ndef ", 5)]
            phone_return = block[block.index('if profile == "phone":') :]
            self.assertIn("device.back()", phone_return)
            self.assertIn("reset_scroll_to_top(device, swipes=12)", phone_return)
            self.assertIn(f'device.wait("{persisted_name}", timeout=60, scroll=True)', phone_return)

    def test_restart_contact_toggle_checks_reset_and_scroll_in_document_order(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        block = source[source.index("def assert_contact_persisted") :]
        block = block[: block.index("\ndef ", 5)]
        toggle_loop = block.index('for toggle in ("group", "family", "blackmail")')
        self.assertLess(
            block.index("reset_collection_editor_to_top(device, profile)"),
            toggle_loop,
        )
        self.assertIn("assert_toggle_state(", block[toggle_loop:])
        helper = source[source.index("def assert_toggle_state") :]
        helper = helper[: helper.index("def selected_text")]
        self.assertIn("max_scrolls=20", helper)
        self.assertIn("scroll_distance_ratio=0.22", helper)

    def test_document_picker_opens_downloads_when_fixture_is_not_recent(self) -> None:
        device = Mock()
        device.find.return_value = None

        DRIVER.select_android_document(device, "linked-runner-e2e.chum5")

        self.assertEqual(
            [
                call("Show roots", timeout=45),
                call("Downloads", timeout=45),
                call("Files in Downloads", timeout=45),
                call("linked-runner-e2e.chum5", timeout=45, scroll=True),
            ],
            device.wait.call_args_list,
        )
        self.assertEqual(
            [
                call("Show roots"),
                call("Downloads"),
                call("linked-runner-e2e.chum5", scroll=True),
            ],
            device.tap.call_args_list,
        )

    def test_document_picker_uses_visible_fixture_without_changing_root(self) -> None:
        device = Mock()
        device.find.side_effect = lambda selector: (
            object() if selector == "linked-runner-e2e.chum5" else None
        )

        DRIVER.select_android_document(device, "linked-runner-e2e.chum5")

        device.wait.assert_not_called()
        device.tap.assert_called_once_with("linked-runner-e2e.chum5", scroll=True)

    def test_document_picker_closes_tablet_roots_drawer_before_fixture_tap(self) -> None:
        device = Mock()
        device.find.side_effect = lambda selector: (
            object()
            if selector in {
                "Recent",
                "Documents",
                "invalid-linked-runner-e2e.chum5",
            }
            else None
        )
        device.display_size.return_value = (2560, 1800)

        with patch.object(DRIVER.time, "sleep") as sleep:
            DRIVER.select_android_document(device, "invalid-linked-runner-e2e.chum5")

        device.shell.assert_called_once_with("input", "tap", "1920", "900")
        sleep.assert_called_once_with(0.75)
        device.tap.assert_called_once_with(
            "invalid-linked-runner-e2e.chum5",
            scroll=True,
        )

    def test_linked_identity_resets_editor_before_reading_top_fields(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        linked_identity = source[source.index("def assert_linked_identity") :]
        linked_identity = linked_identity[: linked_identity.index("def select_android_document")]

        self.assertLess(
            linked_identity.index("reset_scroll_to_top("),
            linked_identity.index("for selector, label, value in expected:"),
        )
        self.assertNotIn("device.wait(selector, scroll=True)", linked_identity)

    def test_persisted_field_reader_requires_exact_selector(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        selected_text = source[source.index("def selected_text") :]
        selected_text = selected_text[: selected_text.index("def assert_linked_identity")]

        self.assertIn("node = device.find(selector)", selected_text)
        self.assertNotIn("field_after_label=label", selected_text)

    def test_swipe_down_stays_inside_phone_display(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.swipe_down()

        self.assertEqual(
            ("input", "swipe", "540", "720", "540", "1968", "300"),
            device.commands[-1],
        )

    def test_tablet_selectors_scroll_their_own_panes(self) -> None:
        self.assertEqual(
            0.15,
            DRIVER.Device._scroll_x_ratio("tablet-build-tab-tab-relationships"),
        )
        self.assertEqual(
            0.15,
            DRIVER.Device._scroll_x_ratio("tablet-build-action-tab-gear-gear"),
        )
        self.assertEqual(
            0.15,
            DRIVER.Device._scroll_x_ratio("tablet-quick-contact-add"),
        )
        self.assertEqual(
            0.82,
            DRIVER.Device._scroll_x_ratio("tablet-inspector-save"),
        )
        self.assertEqual(
            0.82,
            DRIVER.Device._scroll_x_ratio("tablet-attribute-base-body"),
        )

    def test_tablet_attribute_route_uses_visible_inspector(self) -> None:
        class AttributeRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

        device = AttributeRouteDevice()
        DRIVER.open_attribute_section(device, "tablet")

        self.assertEqual(
            ([
                ("swipe", "down"),
            ] * 24) + [
                ("tap", "tablet-build-tab-tab-attributes"),
                ("swipe", "down"),
                ("swipe", "down"),
                ("wait", "tablet-attribute-body"),
                ("tap", "tablet-attribute-body"),
            ],
            device.calls,
        )

    def test_phone_attribute_route_uses_auto_loaded_default_action(self) -> None:
        class AttributeRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []
                self.row_wait_options: dict[str, object] = {}

            def tap_bidirectional(self, selector: str, **_: object) -> None:
                self.calls.append(("tap_bidirectional", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

            def wait(self, selector: str, **options: object) -> None:
                self.calls.append(("wait", selector))
                if selector == "attribute-body":
                    self.row_wait_options = options

        device = AttributeRouteDevice()
        with patch.object(DRIVER.time, "sleep"):
            DRIVER.open_attribute_section(device, "phone")

        self.assertEqual(
            [
                ("tap_bidirectional", "build-section-tab-attributes"),
                ("swipe", "down"),
                ("swipe", "down"),
                ("wait", "attribute-body"),
            ],
            device.calls,
        )
        self.assertEqual(
            {
                "timeout": 120,
                "scroll": True,
                "max_scrolls": 24,
                "scroll_distance_ratio": 0.22,
            },
            device.row_wait_options,
        )

    def test_career_body_improvement_binds_rendered_total_and_karma(self) -> None:
        class CareerAttributeDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []
                self.total = 1

            def tap_bidirectional(self, selector: str, **_: object) -> None:
                self.calls.append(("tap_bidirectional", selector))

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

            def wait(self, selector: str, **_: object) -> DRIVER.UiNode:
                self.calls.append(("wait", selector))
                if selector == "attribute-bod":
                    return DRIVER.UiNode(
                        {"content-desc": f"Body. {self.total} · 1-6 · Aug 10"}
                    )
                return DRIVER.UiNode({"text": selector})

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))
                if selector == "attribute-improve-bod":
                    self.total = 2

            def back(self) -> None:
                self.calls.append(("back", ""))

            def capture(self, name: str) -> None:
                raise AssertionError(f"unexpected capture: {name}")

        device = CareerAttributeDevice()
        contract = DRIVER.FullEditingFixtureContract(1, 2, 10, 35, 25, 15)
        with patch.object(DRIVER.time, "sleep"):
            DRIVER.improve_body_in_career(device, "phone", contract)

        self.assertIn(("tap", "attribute-improve-bod"), device.calls)
        self.assertIn(("wait", "Available Karma: 35"), device.calls)
        self.assertIn(("wait", "Improve · 10 Karma"), device.calls)
        self.assertIn(("wait", "Available Karma: 25"), device.calls)
        self.assertIn(("wait", "Improve · 15 Karma"), device.calls)
        self.assertEqual(2, device.total)

    def test_phone_gear_route_resets_preserved_action_scroll(self) -> None:
        class GearRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, dict[str, object]]] = []

            def tap_bidirectional(self, selector: str, **options: object) -> None:
                self.calls.append(("tap_bidirectional", selector, options))

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **options: object,
            ) -> None:
                self.calls.append(("wait_exact", selector, options))

        device = GearRouteDevice()
        with patch.object(
            DRIVER,
            "return_to_phone_runner_root",
            side_effect=lambda actual, **options: actual.calls.append(
                ("return_to_root", "", options)
            ),
        ):
            DRIVER.open_gear_section(device, "phone")

        self.assertEqual(
            ("return_to_root", "", {"created": True}),
            device.calls[0],
        )
        self.assertEqual(
            (
                "tap_bidirectional",
                "build-section-tab-gear",
                {
                    "timeout": 120,
                    "backward_scrolls": 24,
                    "forward_scrolls": 24,
                    "scroll_distance_ratio": 0.22,
                    "exact_resource_id": True,
                },
            ),
            device.calls[1],
        )
        self.assertEqual(
            [
                (
                    "wait_exact",
                    "section-quick-gear-add",
                    {
                        "timeout": 180,
                        "backward_scrolls": 24,
                        "forward_scrolls": 48,
                        "scroll_distance_ratio": 0.22,
                    },
                ),
            ],
            device.calls[2:],
        )
        self.assertNotIn(
            "build-action-tab-gear-gear",
            [selector for _, selector, _ in device.calls],
        )

    def test_phone_relationship_routes_use_exact_bounded_activation_and_quick_add_reset(self) -> None:
        class RelationshipRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, dict[str, object]]] = []

            def tap_exact_resource_id_bidirectional(
                self,
                selector: str,
                **options: object,
            ) -> None:
                self.calls.append(("tap_exact", selector, options))

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **options: object,
            ) -> None:
                self.calls.append(("wait_exact", selector, options))

            def swipe_down(self, **options: object) -> None:
                self.calls.append(("swipe_down", "", options))

            def wait(self, selector: str, **options: object) -> DRIVER.UiNode:
                self.calls.append(("wait", selector, options))
                return DRIVER.UiNode({"text": selector})

        for opener, action_selector in (
            (DRIVER.open_contact_section, "build-action-tab-relationships-contacts"),
            (DRIVER.open_pet_section, "build-action-tab-relationships-pets"),
        ):
            with self.subTest(action_selector=action_selector):
                device = RelationshipRouteDevice()
                with patch.object(DRIVER.time, "sleep"):
                    opener(device, "phone")

                self.assertEqual(
                    [
                        (
                            "tap_exact",
                            "build-section-tab-relationships",
                            {
                                "timeout": 120,
                                "backward_scrolls": 24,
                                "forward_scrolls": 24,
                                "scroll_distance_ratio": 0.22,
                                "evidence_prefix": "relationships-section-route",
                                "surface_name": "Relationships section route",
                            },
                        ),
                        (
                            "tap_exact",
                            action_selector,
                            {
                                "timeout": 180,
                                "backward_scrolls": 24,
                                "forward_scrolls": 48,
                                "scroll_distance_ratio": 0.22,
                                "evidence_prefix": "relationships-collection-route",
                                "surface_name": "Relationships collection route",
                            },
                        ),
                        (
                            "wait",
                            "No entries yet. Use an action above to add one.",
                            {
                                "timeout": 60,
                                "scroll": True,
                                "max_scrolls": 24,
                                "scroll_distance_ratio": 0.22,
                            },
                        ),
                        (
                            "wait_exact",
                            "section-quick-contact-add",
                            {
                                "timeout": 180,
                                "backward_scrolls": 24,
                                "forward_scrolls": 48,
                                "scroll_distance_ratio": 0.22,
                            },
                        ),
                    ],
                    [
                        call_value
                        for call_value in device.calls
                        if call_value[0] != "swipe_down"
                    ],
                )
                marker_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0] == "wait"
                )
                quick_add_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0] == "wait_exact"
                )
                self.assertEqual(
                    24,
                    sum(
                        call_value[0] == "swipe_down"
                        for call_value in device.calls[1:marker_index]
                    ),
                )
                self.assertLess(marker_index, quick_add_index)

    def test_stale_relationships_quick_add_cannot_satisfy_collection_activation(self) -> None:
        class StaleRelationshipsDevice:
            def __init__(self) -> None:
                self.stale_quick_add_visible = True
                self.quick_add_waited = False

            def tap_exact_resource_id_bidirectional(
                self,
                _: str,
                **__: object,
            ) -> None:
                return None

            def swipe_down(self, **_: object) -> None:
                return None

            def wait(self, selector: str, **_: object) -> DRIVER.UiNode:
                if selector == "No entries yet. Use an action above to add one.":
                    raise RuntimeError("target collection empty marker absent on stale Relationships")
                raise AssertionError(f"unexpected selector {selector!r}")

            def wait_exact_resource_id_bidirectional(
                self,
                selector: str,
                **_: object,
            ) -> None:
                if selector == "section-quick-contact-add" and self.stale_quick_add_visible:
                    self.quick_add_waited = True
                    return None
                raise AssertionError(f"unexpected selector {selector!r}")

        device = StaleRelationshipsDevice()
        with (
            patch.object(DRIVER.time, "sleep"),
            self.assertRaisesRegex(RuntimeError, "empty marker absent on stale Relationships"),
        ):
            DRIVER._open_phone_relationship_collection(
                device,
                action_selector="build-action-tab-relationships-contacts",
                quick_add_selector="section-quick-contact-add",
                expected_item=None,
            )

        self.assertFalse(device.quick_add_waited)

    def test_phone_relationship_fixture_route_resets_before_bounded_item_search(self) -> None:
        class RelationshipFixtureDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, dict[str, object]]] = []

            def tap_exact_resource_id_bidirectional(
                self,
                selector: str,
                **options: object,
            ) -> None:
                self.calls.append(("tap_exact", selector, options))

            def swipe_down(self, **options: object) -> None:
                self.calls.append(("swipe_down", "", options))

            def wait(self, selector: str, **options: object) -> None:
                self.calls.append(("wait", selector, options))

        for opener, action_selector, expected_item in (
            (
                DRIVER.open_contact_section,
                "build-action-tab-relationships-contacts",
                "ContactPersistedE2E",
            ),
            (
                DRIVER.open_pet_section,
                "build-action-tab-relationships-pets",
                "PetPersistedE2E",
            ),
        ):
            with self.subTest(action_selector=action_selector):
                device = RelationshipFixtureDevice()
                with patch.object(DRIVER.time, "sleep"):
                    opener(device, "phone", expected_item=expected_item)

                self.assertEqual(
                    [
                        "build-section-tab-relationships",
                        action_selector,
                    ],
                    [
                        selector
                        for kind, selector, _ in device.calls
                        if kind == "tap_exact"
                    ],
                )
                action_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0:2] == ("tap_exact", action_selector)
                )
                wait_index = next(
                    index
                    for index, call_value in enumerate(device.calls)
                    if call_value[0:2] == ("wait", expected_item)
                )
                between = device.calls[action_index + 1 : wait_index]
                self.assertEqual(24, sum(call_value[0] == "swipe_down" for call_value in between))
                self.assertEqual(
                    (
                        "wait",
                        expected_item,
                        {
                            "timeout": 60,
                            "scroll": True,
                            "max_scrolls": 24,
                            "scroll_distance_ratio": 0.22,
                        },
                    ),
                    device.calls[wait_index],
                )

    def test_exact_bidirectional_wait_recovers_quick_add_above_preserved_scroll(self) -> None:
        wrong_prefix = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "build-action-tab-gear-gearlocations"
                ),
                "clickable": "true",
                "bounds": "[100,100][300,300]",
            }
        )
        finder = Mock(spec=DRIVER.Device)
        finder.hierarchy.return_value = [wrong_prefix]
        self.assertTrue(
            DRIVER.Device._matches(wrong_prefix, "build-action-tab-gear-gear")
        )
        self.assertIsNone(
            DRIVER.Device.find_exact_resource_id(
                finder,
                "build-action-tab-gear-gear",
            )
        )

        target = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/section-quick-gear-add",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[100,100][300,300]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        preserved_offset = {"rows_below_top": 24}
        device._scroll_x_ratio.return_value = 0.5
        device.swipe_down.side_effect = lambda **_: preserved_offset.update(
            rows_below_top=max(0, preserved_offset["rows_below_top"] - 1)
        )
        device.swipe_up.side_effect = lambda **_: preserved_offset.update(
            rows_below_top=preserved_offset["rows_below_top"] + 1
        )
        device.hierarchy.side_effect = lambda: (
            [target] if preserved_offset["rows_below_top"] == 0 else []
        )
        device.node_has_tappable_bounds.return_value = True
        device.dismiss_system_ui_anr.return_value = False
        with patch.object(DRIVER.time, "sleep"):
            actual = DRIVER.Device.wait_exact_resource_id_bidirectional(
                device,
                "section-quick-gear-add",
                timeout=120,
                backward_scrolls=24,
                forward_scrolls=24,
                scroll_distance_ratio=0.22,
            )

        self.assertIs(target, actual)
        self.assertEqual(0, preserved_offset["rows_below_top"])
        self.assertEqual(24, device.swipe_down.call_count)
        device.swipe_up.assert_not_called()
        device.node_has_tappable_bounds.assert_called_once_with(target)

        old_offset = {"rows_below_top": 24}
        old_device = Mock(spec=DRIVER.Device)
        old_device.find.side_effect = lambda _: (
            target if old_offset["rows_below_top"] == 0 else None
        )
        old_device.dismiss_system_ui_anr.return_value = False
        old_device._scroll_x_ratio.return_value = 0.5
        old_device.swipe_up.side_effect = lambda **_: old_offset.update(
            rows_below_top=old_offset["rows_below_top"] + 1
        )
        clock = {"now": 0.0}

        def advance_clock() -> float:
            clock["now"] += 1.0
            return clock["now"]

        with patch.object(DRIVER.time, "monotonic", side_effect=advance_clock), patch.object(
            DRIVER.time,
            "sleep",
        ):
            with self.assertRaisesRegex(RuntimeError, "Timed out waiting"):
                DRIVER.Device.wait(
                    old_device,
                    "section-quick-gear-add",
                    timeout=4,
                    scroll=True,
                    max_scrolls=4,
                    scroll_distance_ratio=0.22,
                )
        self.assertGreater(old_offset["rows_below_top"], 24)

    def test_exact_bidirectional_tap_uses_the_cardinality_checked_waited_node(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "build-section-tab-relationships"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,420][984,640]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device.wait_exact_resource_id_bidirectional.return_value = target

        DRIVER.Device.tap_exact_resource_id_bidirectional(
            device,
            "build-section-tab-relationships",
            timeout=120,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="relationships-section-route",
            surface_name="Relationships section route",
        )

        device.wait_exact_resource_id_bidirectional.assert_called_once_with(
            "build-section-tab-relationships",
            timeout=120,
            backward_scrolls=24,
            forward_scrolls=24,
            scroll_distance_ratio=0.22,
            evidence_prefix="relationships-section-route",
            surface_name="Relationships section route",
        )
        device.hierarchy.assert_not_called()
        device.shell.assert_called_once_with("input", "tap", "541", "530")

    def test_exact_bidirectional_wait_does_not_scroll_after_empty_hierarchy(self) -> None:
        target = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "creation-prerequisite-category-heritage"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,500][984,720]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device._scroll_x_ratio.return_value = 0.5
        device.hierarchy.side_effect = [[], [target]]
        device.node_has_tappable_bounds.return_value = True

        with patch.object(DRIVER.time, "sleep"):
            actual = DRIVER.Device.wait_exact_resource_id_bidirectional(
                device,
                "creation-prerequisite-category-heritage",
                backward_scrolls=0,
                forward_scrolls=4,
            )

        self.assertIs(target, actual)
        self.assertEqual(2, device.hierarchy.call_count)
        device.swipe_up.assert_not_called()
        device.swipe_down.assert_not_called()
        device.dismiss_system_ui_anr.assert_not_called()

    def test_exact_bidirectional_wait_backtracks_after_clipped_overshoot(self) -> None:
        decoy = DRIVER.UiNode(
            {
                "resource-id": "com.myexternalbrain.chummer:id/decoy",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,500][984,720]",
            }
        )
        clipped = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "creation-prerequisite-category-heritage"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,275][984,276]",
            }
        )
        target = DRIVER.UiNode(
            {
                "resource-id": (
                    "com.myexternalbrain.chummer:id/"
                    "creation-prerequisite-category-heritage"
                ),
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,420][984,640]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        position = {"value": 0}
        device._scroll_x_ratio.return_value = 0.5
        device.display_size.return_value = (1080, 2400)
        device.hierarchy.side_effect = lambda: (
            [decoy]
            if position["value"] == 0
            else [clipped]
            if position["value"] == 2
            else [target]
        )
        device.swipe_up.side_effect = lambda **_: position.update(value=2)
        device.swipe_down.side_effect = lambda **_: position.update(value=1)
        device.node_has_tappable_bounds.side_effect = (
            lambda node: DRIVER.Device.node_has_tappable_bounds(device, node)
        )
        device.dismiss_system_ui_anr.return_value = False

        with patch.object(DRIVER.time, "sleep"):
            actual = DRIVER.Device.wait_exact_resource_id_bidirectional(
                device,
                "creation-prerequisite-category-heritage",
                backward_scrolls=0,
                forward_scrolls=4,
                scroll_distance_ratio=0.22,
            )

        self.assertIs(target, actual)
        self.assertEqual(1, device.swipe_up.call_count)
        self.assertEqual(1, device.swipe_down.call_count)
        self.assertEqual(1, position["value"])
        device.capture.assert_not_called()

    def test_exact_bidirectional_wait_rejects_duplicate_resource_ids(self) -> None:
        duplicate = DRIVER.UiNode(
            {
                "resource-id": "creation-prerequisite-category-heritage",
                "enabled": "true",
                "clickable": "true",
                "bounds": "[98,420][984,640]",
            }
        )
        device = Mock(spec=DRIVER.Device)
        device._scroll_x_ratio.return_value = 0.5
        device.hierarchy.return_value = [duplicate, duplicate]

        with patch.object(DRIVER.time, "sleep"), self.assertRaisesRegex(
            RuntimeError,
            "cardinality 2",
        ):
            DRIVER.Device.wait_exact_resource_id_bidirectional(
                device,
                "creation-prerequisite-category-heritage",
                backward_scrolls=0,
                evidence_prefix="creation-prerequisite-heritage-category-row",
            )

        device.capture.assert_called_once_with(
            "creation-prerequisite-heritage-category-row-cardinality-invalid"
        )
        device.swipe_up.assert_not_called()
        device.swipe_down.assert_not_called()

    def test_phone_condition_route_uses_overlapping_scrolls(self) -> None:
        class ConditionRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

        device = ConditionRouteDevice()
        DRIVER.open_condition_monitor_section(device, "phone")

        self.assertEqual(
            [
                ("tap", "build-section-tab-combat"),
                ("tap", "build-action-tab-combat-conditionmonitor"),
                ("wait", "condition-monitor-physical"),
            ],
            device.calls,
        )

    def test_tablet_condition_route_resets_the_persistent_navigation_rail(self) -> None:
        class ConditionRouteDevice:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str]] = []

            def swipe_down(self, **_: object) -> None:
                self.calls.append(("swipe", "down"))

            def tap(self, selector: str, **_: object) -> None:
                self.calls.append(("tap", selector))

            def wait(self, selector: str, **_: object) -> None:
                self.calls.append(("wait", selector))

        device = ConditionRouteDevice()
        with patch.object(DRIVER.time, "sleep"):
            DRIVER.open_condition_monitor_section(device, "tablet")

        self.assertEqual([("swipe", "down")] * 24, device.calls[:24])
        self.assertEqual(
            [
                ("tap", "tablet-build-tab-tab-combat"),
                ("tap", "tablet-build-action-tab-combat-conditionmonitor"),
                ("wait", "tablet-condition-track-physical"),
            ],
            device.calls[24:],
        )

    def test_condition_journey_imports_a_career_fixture_through_the_document_picker(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        condition_branch = source[
            source.index('if args.journey in {"condition-monitor", "contact-pet"}:') :
        ]
        condition_branch = condition_branch[: condition_branch.index("else:")]

        self.assertIn('device.tap("home-open-file")', condition_branch)
        self.assertIn(
            '"career-condition-monitor-e2e.chum5"',
            condition_branch,
        )
        self.assertIn("select_android_document(device, fixture_name)", condition_branch)
        self.assertNotIn("home-new-runner", condition_branch)

        fixture = REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
        fixture_xml = fixture.read_text(encoding="utf-8")
        for marker in (
            "<created>True</created>",
            "<karma>35</karma>",
            "<nuyen>8500</nuyen>",
            "<physicalcm>10</physicalcm>",
            "<physicalcmoverflow>3</physicalcmoverflow>",
            "<stuncm>10</stuncm>",
        ):
            self.assertIn(marker, fixture_xml)

    def test_contact_pet_journey_is_focused_and_process_restart_bound(self) -> None:
        source = Path(DRIVER.__file__).read_text(encoding="utf-8")
        branch = source[source.index('if args.journey == "contact-pet":') :]
        branch = branch[: branch.index('if args.journey == "condition-monitor":')]

        for marker in (
            "add_and_edit_contact(device, args.profile, create_items=False)",
            "edit_creation_free_contact(device, args.profile)",
            "add_and_edit_pet(device, args.profile, create_items=False)",
            "force_stop_and_launch_new_process(",
            "assert_contact_persisted(device, args.profile)",
            "assert_creation_free_contact_persisted(device, args.profile)",
            "assert_pet_persisted(device, args.profile)",
            '"inputFixtureSha256": contact_pet_runner_sha256',
            '"verifiedRemoteInputFixtureSha256": verified_remote_sha256[',
            '"preRestartAuthority": optional_workspace_authority_json(persisted_authority)',
            '"postRestartAuthority": optional_workspace_authority_json(restored_authority)',
            '"processRestartContactPersistence": "pass"',
            '"creationContactFreeIsolatedPersisted": "pass"',
            '"processRestartPetPersistence": "pass"',
        ):
            self.assertIn(marker, branch)
        self.assertNotIn("attach_linked_runner(", branch)

        fixture = REPO_ROOT / "tests" / "fixtures" / "creation-contact-pet-e2e.chum5"
        fixture_xml = fixture.read_text(encoding="utf-8")
        for marker in (
            "<created>False</created>",
            "<name>ContactE2E</name>",
            "<name>ContactDeleteE2E</name>",
            "<name>ContactFreePersistedE2E</name>",
            "<name>PetE2E</name>",
            "<name>PetDeleteE2E</name>",
            "<type>Contact</type>",
            "<type>Pet</type>",
        ):
            self.assertIn(marker, fixture_xml)

    def test_phone_origin_dossier_resets_before_overlapping_search(self) -> None:
        device = Mock()

        with patch.object(DRIVER.time, "sleep"):
            DRIVER.open_origin_dossier(device, "phone")

        self.assertEqual(12, device.swipe_down.call_count)
        device.tap.assert_called_once_with(
            "build-origin-dossier",
            scroll=True,
            timeout=60,
            max_scrolls=16,
            scroll_distance_ratio=0.22,
        )

    def test_condition_damage_uses_distinct_phone_and_tablet_editors(self) -> None:
        with patch.object(DRIVER, "open_condition_monitor_section"), patch.object(
            DRIVER, "selected_text", return_value="2"
        ), patch.object(DRIVER.time, "sleep"):
            phone = Mock()
            DRIVER.edit_condition_damage(phone, "phone", "physical", 2)
            tablet = Mock()
            DRIVER.edit_condition_damage(tablet, "tablet", "physical", 2)

        phone.assert_has_calls(
            [
                call.tap("condition-monitor-physical", scroll=True),
                call.wait("condition-monitor-editor-physical", timeout=45),
                call.tap("condition-monitor-filled-physical", scroll=True),
                call.tap("2", scroll=True),
                call.tap("condition-monitor-save-physical", scroll=True),
                call.back(),
                call.back(),
            ]
        )
        tablet.assert_has_calls(
            [
                call.tap("tablet-condition-track-physical", scroll=True),
                call.tap("tablet-condition-filled-physical", scroll=True),
                call.tap("2", scroll=True),
                call.tap("tablet-condition-save-physical", scroll=True),
            ]
        )

    def test_gear_journey_edits_name_before_descending_to_add_action(self) -> None:
        device = Mock()
        device.assert_text = Mock()

        with patch.object(DRIVER, "return_to_phone_runner_root"):
            DRIVER.add_and_edit_gear(device, "phone")

        name_edit = device.method_calls.index(
            call.set_text(
                "dialog-field-uigearname",
                "Gear Name",
                "Ares Predator V",
                scroll=True,
                max_scrolls=32,
                scroll_distance_ratio=0.28,
            )
        )
        add_tap = device.method_calls.index(
            call.tap(
                "dialog-action-add",
                scroll=True,
                timeout=180,
                max_scrolls=48,
                scroll_distance_ratio=0.28,
            )
        )
        self.assertLess(name_edit, add_tap)
        self.assertLess(
            add_tap,
            device.method_calls.index(
                call.tap(
                    "Ares Predator V",
                    scroll=True,
                    timeout=60,
                    max_scrolls=24,
                    scroll_distance_ratio=0.22,
                    text_leading_offset=18,
                )
            ),
        )
        add_index = add_tap
        gear_index = device.method_calls.index(
            call.tap(
                "Ares Predator V",
                scroll=True,
                timeout=60,
                max_scrolls=24,
                scroll_distance_ratio=0.22,
                text_leading_offset=18,
            )
        )
        self.assertEqual(
            6,
            device.method_calls[add_index:gear_index].count(call.swipe_down(x_ratio=0.5)),
        )

    def test_back_uses_explicit_app_navigation_when_available(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            device.nodes = [
                DRIVER.UiNode(
                    {
                        "content-desc": "Navigate up",
                        "clickable": "true",
                        "bounds": "[0,128][147,275]",
                    }
                )
            ]
            with patch.object(DRIVER.time, "sleep") as sleep:
                device.back()

        self.assertEqual(("input", "tap", "73", "201"), device.commands[-1])
        sleep.assert_called_once_with(1)

    def test_assert_text_retries_a_transient_empty_hierarchy(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            device = RecordingDevice(Path(temporary), "Physical size: 1080x2400")
            rendered = DRIVER.UiNode({"text": "NativeE2E"})
            with patch.object(
                device,
                "hierarchy",
                side_effect=[[], [rendered]],
            ) as hierarchy, patch.object(
                DRIVER.time,
                "monotonic",
                side_effect=[0, 0, 1],
            ), patch.object(DRIVER.time, "sleep") as sleep:
                device.assert_text("NativeE2E")

        self.assertEqual(2, hierarchy.call_count)
        sleep.assert_called_once_with(0.5)


if __name__ == "__main__":
    unittest.main()
