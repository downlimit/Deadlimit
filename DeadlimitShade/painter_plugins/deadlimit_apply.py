"""Deadlimit Shade panel for applying a character profile to the open project."""

import json
import hashlib
import os
import tempfile
from pathlib import Path

from PySide2 import QtCore, QtWidgets

import substance_painter.js
import substance_painter.display
import substance_painter.export
import substance_painter.project
import substance_painter.resource
import substance_painter.textureset
import substance_painter.ui

try:
    import substance_painter.layerstack as painter_layerstack
    import substance_painter.colormanagement as painter_colormanagement
except ImportError:
    # Painter 9.1 ships API 0.2.11. Official layer creation arrived in 10.0.
    painter_layerstack = None
    painter_colormanagement = None


PLUGIN_WIDGETS = []
RIM_MASK_CHANNEL = substance_painter.textureset.ChannelType.User0
RIM_MASK_LABEL = "Deadlimit Rim Mask"
ARTISTIC_AO_CHANNEL = substance_painter.textureset.ChannelType.User1
ARTISTIC_AO_LABEL = "Deadlimit Artistic AO"
ARTISTIC_AO_BASE_LABEL = "Deadlimit Artistic AO Base"


def _shade_root():
    configured = os.environ.get("DEADLIMIT_SHADE_ROOT")
    if configured:
        return Path(configured).resolve()
    source_root = Path(__file__).resolve().parents[1]
    if (source_root / "profiles").is_dir() and (source_root / "tools").is_dir():
        return source_root
    installed_runtime = Path(__file__).resolve().with_name("deadlimit_apply_runtime")
    if (installed_runtime / "profiles").is_dir() and (installed_runtime / "tools").is_dir():
        return installed_runtime
    raise RuntimeError("Deadlimit Shade runtime was not found beside the Painter plugin")


def _profiles():
    result = []
    for path in sorted((_shade_root() / "profiles").glob("*.json")):
        if path.name == "schema.json":
            continue
        profile = json.loads(path.read_text(encoding="utf-8"))
        if "painterApply" in profile:
            result.append(profile)
    return sorted(result, key=lambda item: int(item["id"]))


def _lighting_presets():
    document = json.loads(
        (_shade_root() / "lighting" / "preview-presets.json").read_text(encoding="utf-8"))
    source = {item["name"]: item for item in document["presets"]}
    resolved = []
    for item in document["presets"]:
        value = dict(source[item["inherits"]]) if item.get("inherits") else {}
        value.update(item)
        resolved.append(value)
    return resolved


def _environment_options():
    """CSDK panoramas proven by the Lighting Preview manifest and local depot."""
    content_root = _csdk_content_root()
    options = []
    seen = set()
    for preset in _lighting_presets():
        source = (preset.get("environment") or {}).get("sourceImage")
        if not source or source in seen:
            continue
        if content_root and (content_root / Path(source.replace("/", os.sep))).is_file():
            options.append((preset["name"], source))
            seen.add(source)
    return options


def _csdk_content_root():
    configured = os.environ.get("DEADLIMIT_CSDK_ROOT")
    candidates = []
    if configured:
        candidates.append(Path(configured))
    candidates.append(Path(r"C:\WorkProjects\Deadlock\Reduced_CSDK_12"))
    for candidate in candidates:
        content = candidate / "content" / "citadel"
        if content.is_dir():
            return content
    return None


def _lighting_shader_parameters(preset, environment_bound):
    lights = preset.get("lights", [])
    key = lights[0] if lights else {}
    fill = lights[1] if len(lights) > 1 else {}
    headlight = preset.get("headlight") or {}
    rim = preset.get("rim") or {}
    return {
        "dl_lighting_preset_mode": True,
        "dl_preset_key_direction": key.get("direction", [0.0, 1.0, 0.0]),
        "dl_preset_key_color": key.get("color", [1.0, 1.0, 1.0]),
        "dl_preset_key_intensity": float(key.get("intensity") or 0.0),
        "dl_preset_key_casts_shadows": bool(key.get("castsShadows", False)),
        "dl_preset_fill_direction": fill.get("direction", [0.0, 1.0, 0.0]),
        "dl_preset_fill_color": fill.get("color", [1.0, 1.0, 1.0]),
        "dl_preset_fill_intensity": float(fill.get("intensity") or 0.0),
        "dl_preset_headlight_enabled": bool(headlight.get("intensity")),
        "dl_preset_headlight_color": headlight.get("color") or [1.0, 1.0, 1.0],
        "dl_preset_headlight_intensity": float(headlight.get("intensity") or 0.0),
        "dl_preset_headlight_casts_shadows": bool(
            headlight.get("castsShadows", False)),
        "dl_preset_rim_enabled": bool(rim.get("enabled", False)),
        "dl_preset_rim_cutoff": float(rim.get("cutoff", 1.0)),
        "dl_preset_rim_sharpness": float(rim.get("sharpness", 0.01)),
        "dl_preset_rim_strength": float(rim.get("strength", 0.0)),
        "dl_preset_rim_up_ramp": rim.get("upRamp", [-1.0, 1.0]),
        "dl_environment_specular_enabled": bool(environment_bound),
        "dl_captured_environment": False,
    }


def _rim_shader_parameters(profile):
    rim = profile["rim"]
    return {
        "dl_npr_rim_lighting": bool(rim["enabled"]),
        "dl_npr_rim_cutoff": float(rim["cutoff"]),
        "dl_npr_rim_sharpness": float(rim["sharpness"]),
        "dl_npr_rim_strength": float(rim["strength"]),
        "dl_npr_rim_up_ramp_start": float(rim["upRamp"][0]),
        "dl_npr_rim_up_ramp_end": float(rim["upRamp"][1]),
    }


def _converter_path():
    candidates = [
        _shade_root() / "tools" / "Deadlimit.MeshPreview.exe",
        _shade_root() / "tools" / "Deadlimit.MeshPreview" / "bin" / "Release" / "net10.0" / "win-x64" / "publish" / "Deadlimit.MeshPreview.exe",
        _shade_root() / "tools" / "Deadlimit.MeshPreview" / "bin" / "Release" / "net10.0" / "win-x64" / "Deadlimit.MeshPreview.exe",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise RuntimeError("Deadlimit.MeshPreview.exe was not found in the plugin runtime")


def _retail_converter_path():
    candidates = [
        _shade_root() / "tools" / "Deadlimit.RetailTextures.exe",
        _shade_root() / "tools" / "Deadlimit.RetailTextures" / "bin" / "Release" / "net10.0" / "win-x64" / "publish" / "Deadlimit.RetailTextures.exe",
        _shade_root() / "tools" / "Deadlimit.RetailTextures" / "bin" / "Release" / "net10.0" / "Deadlimit.RetailTextures.exe",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise RuntimeError("Deadlimit.RetailTextures.exe was not found in the plugin runtime")


def _project_context(source_mesh):
    for directory in [source_mesh.parent] + list(source_mesh.parents):
        manifest_path = directory / ".deadlimit" / "project.json"
        if not manifest_path.is_file():
            continue
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        retail_vpk = manifest.get("RetailSourceVpk")
        if retail_vpk:
            return {
                "projectRoot": directory,
                "sourceRoot": directory / manifest.get("SourceDumpFolderName", "0source"),
                "retailVpk": Path(retail_vpk),
            }
    return None


def _import_or_reuse_project_texture(path, name):
    identifier = substance_painter.resource.ResourceID.from_project(name)
    existing = substance_painter.resource.Resource.retrieve(identifier)
    if existing:
        return existing[0]
    return substance_painter.resource.import_project_resource(
        str(path),
        substance_painter.resource.Usage.TEXTURE,
        name=name,
        group="Deadlimit Retail Preview")


def _import_or_reuse_project_environment(path, name):
    identifier = substance_painter.resource.ResourceID.from_project(name)
    existing = substance_painter.resource.Resource.retrieve(identifier)
    if existing:
        return existing[0]
    return substance_painter.resource.import_project_resource(
        str(path),
        substance_painter.resource.Usage.ENVIRONMENT,
        name=name,
        group="Deadlimit Retail Environments")


def _import_or_reuse_project_shader(path, role):
    digest = hashlib.sha256(path.read_bytes()).hexdigest()[:12]
    name = "Deadlimit_{}_{}".format(role, digest)
    identifier = substance_painter.resource.ResourceID.from_project(name)
    existing = substance_painter.resource.Resource.retrieve(identifier)
    if existing:
        resource = existing[0]
    else:
        resource = substance_painter.resource.import_project_resource(
            str(path),
            substance_painter.resource.Usage.SHADER,
            name=name,
            group="Deadlimit Shade")
    return {"name": name, "url": resource.identifier().url()}


def _shader_assignment_script(profile, retail_bindings=None, shader_urls=None,
                              lighting_parameters=None, rim_parameters=None,
                              artistic_ao_texture_sets=None):
    character_id = int(profile["id"])
    hero_texture_sets = json.dumps(profile["painterApply"]["heroTextureSets"])
    retail_bindings_json = json.dumps(retail_bindings or {})
    shader_urls_json = json.dumps(shader_urls or {})
    lighting_parameters = dict(lighting_parameters or {})
    lighting_preset_name = lighting_parameters.pop("presetName", "")
    lighting_parameters_json = json.dumps(lighting_parameters)
    rim_parameters_json = json.dumps(rim_parameters or _rim_shader_parameters(profile))
    artistic_ao_texture_sets_json = json.dumps(artistic_ao_texture_sets or [])
    return r"""
(function() {
  alg.resources.refreshShelves();
  var current = alg.shaders.shaderInstancesToObject();
  var names = Object.keys(current.shaders);
  if (names.length === 0) {
    throw new Error("Painter project has no source shader instance");
  }
  var heroTextureSets = HERO_TEXTURE_SETS;
  var retailBindings = RETAIL_BINDINGS;
  var shaderResources = SHADER_RESOURCES;
  var lightingParameters = LIGHTING_PARAMETERS;
  var rimDefaults = RIM_PARAMETERS;
  var artisticAoTextureSets = ARTISTIC_AO_TEXTURE_SETS;
  var rimParameterNames = Object.keys(rimDefaults);
  var previousInstances = alg.shaders.instances();
  function parameterValue(parameters, name) {
    var parameter = parameters[name];
    return parameter && Object.prototype.hasOwnProperty.call(parameter, "value")
      ? parameter.value
      : undefined;
  }
  function preservedRim(label) {
    var matches = previousInstances.filter(function(item) { return item.label === label; });
    var instance = matches[matches.length - 1];
    if (!instance) return {};
    var parameters = alg.shaders.parameters(instance.id);
    if (parameterValue(parameters, "dl_character") !== CHARACTER_ID) return {};
    var result = {};
    rimParameterNames.forEach(function(name) {
      var value = parameterValue(parameters, name);
      if (value !== undefined) result[name] = value;
    });
    return result;
  }
  function rimParametersFor(label) {
    var result = {};
    rimParameterNames.forEach(function(name) { result[name] = rimDefaults[name]; });
    var preserved = preservedRim(label);
    Object.keys(preserved).forEach(function(name) { result[name] = preserved[name]; });
    return result;
  }
  var previousHeroRim = rimParametersFor("Deadlimit Hero");
  var previousRetailRim = {};
  Object.keys(retailBindings).forEach(function(textureSetName) {
    previousRetailRim[retailBindings[textureSetName].instance] =
      rimParametersFor(retailBindings[textureSetName].instance);
  });
  var sourceLabel = null;
  heroTextureSets.some(function(name) {
    if (current.texturesets[name]) {
      sourceLabel = current.texturesets[name].shader;
      return true;
    }
    return false;
  });
  var source = current.shaders[sourceLabel] || current.shaders[names[0]];
  var hero = JSON.parse(JSON.stringify(source));
  hero.shader = shaderResources.hero.name;
  hero.shaderInstance = "Deadlimit Hero";
  hero.parameters = {};
  var outline = JSON.parse(JSON.stringify(source));
  outline.shader = shaderResources.outline.name;
  outline.shaderInstance = "Deadlimit Outline";
  outline.parameters = {};

  var textureSets = {};
  Object.keys(current.texturesets).forEach(function(name) {
    textureSets[name] = {
      shader: name === "__deadlimit_outline"
        ? "Deadlimit Outline"
        : (retailBindings[name]
          ? retailBindings[name].instance
          : (heroTextureSets.indexOf(name) >= 0 ? "Deadlimit Hero" : current.texturesets[name].shader))
    };
  });
  if (!textureSets.__deadlimit_outline || Object.keys(textureSets).length < 2) {
    throw new Error("Generated mesh is missing the __deadlimit_outline Texture Set");
  }

  var shaders = JSON.parse(JSON.stringify(current.shaders));
  shaders["Deadlimit Hero"] = hero;
  shaders["Deadlimit Outline"] = outline;
  Object.keys(retailBindings).forEach(function(textureSetName) {
    if (!current.texturesets[textureSetName]) {
      return;
    }
    var binding = retailBindings[textureSetName];
    var originalLabel = current.texturesets[textureSetName].shader;
    var retail = JSON.parse(JSON.stringify(current.shaders[originalLabel] || source));
    retail.shader = shaderResources.hero.name;
    retail.shaderInstance = binding.instance;
    retail.parameters = {};
    shaders[binding.instance] = retail;
  });
  alg.shaders.shaderInstancesFromObject({
    format: current.format,
    shaders: shaders,
    texturesets: textureSets
  });
  var instances = alg.shaders.instances();
  var heroMatches = instances.filter(function(item) {
    return item.label === "Deadlimit Hero";
  });
  var outlineMatches = instances.filter(function(item) {
    return item.label === "Deadlimit Outline";
  });
  // Painter 9.1 can retain an unused earlier instance with the same label
  // until the project is reopened. shaderInstancesFromObject appends the
  // mapped replacement, so configure the final matching instance.
  var heroInstance = heroMatches[heroMatches.length - 1];
  var outlineInstance = outlineMatches[outlineMatches.length - 1];
  if (!heroInstance || !outlineInstance) {
    throw new Error("Deadlimit shader instances were not created");
  }
  var heroParameters = {
    dl_character: CHARACTER_ID,
    dl_debug_view: 0,
    dl_lighting_input_mode: 0,
    dl_artistic_ao_present: heroTextureSets.filter(function(name) {
      return !!current.texturesets[name];
    }).every(function(name) { return artisticAoTextureSets.indexOf(name) >= 0; })
  };
  Object.keys(lightingParameters).forEach(function(name) {
    heroParameters[name] = lightingParameters[name];
  });
  Object.keys(previousHeroRim).forEach(function(name) {
    heroParameters[name] = previousHeroRim[name];
  });
  alg.shaders.setParameters(heroInstance.id, heroParameters);
  alg.shaders.setParameters(outlineInstance.id, {
    dl_outline_character: CHARACTER_ID,
    dl_outline_use_character_color: true
  });
  Object.keys(retailBindings).forEach(function(textureSetName) {
    if (!current.texturesets[textureSetName]) {
      return;
    }
    var binding = retailBindings[textureSetName];
    var matches = instances.filter(function(item) {
      return item.label === binding.instance;
    });
    var instance = matches[matches.length - 1];
    if (!instance) {
      throw new Error("Retail shader instance was not created for " + textureSetName);
    }
    var retailParameters = {
      dl_character: CHARACTER_ID,
      dl_debug_view: 0,
      dl_lighting_input_mode: 0,
      dl_artistic_ao_present: artisticAoTextureSets.indexOf(textureSetName) >= 0,
      dl_use_retail_inputs: true,
      dl_vertex_color_multiply: binding.vertexColorMultiply,
      dl_retail_color: binding.color,
      dl_retail_normal_roughness: binding.normalRoughness,
      dl_retail_ambient_occlusion: binding.ambientOcclusion,
      dl_retail_tint_rim: binding.tintRim,
      dl_retail_npr_transmissive: binding.nprTransmissive
    };
    Object.keys(lightingParameters).forEach(function(name) {
      retailParameters[name] = lightingParameters[name];
    });
    Object.keys(previousRetailRim[binding.instance]).forEach(function(name) {
      retailParameters[name] = previousRetailRim[binding.instance][name];
    });
    alg.shaders.setParameters(instance.id, retailParameters);
  });
  return JSON.stringify({
    characterId: CHARACTER_ID,
    heroShader: heroInstance.shader,
    outlineShader: outlineInstance.shader,
    textureSets: Object.keys(textureSets).sort(),
    lightingPreset: LIGHTING_PRESET_NAME
  });
})()
""".replace("CHARACTER_ID", str(character_id)).replace("HERO_TEXTURE_SETS", hero_texture_sets).replace("RETAIL_BINDINGS", retail_bindings_json).replace("SHADER_RESOURCES", shader_urls_json).replace("LIGHTING_PARAMETERS", lighting_parameters_json).replace("RIM_PARAMETERS", rim_parameters_json).replace("ARTISTIC_AO_TEXTURE_SETS", artistic_ao_texture_sets_json).replace("LIGHTING_PRESET_NAME", json.dumps(lighting_preset_name))


class DeadlimitApplyDock(QtWidgets.QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Deadlimit Shade")
        self.setObjectName("DeadlimitShadeApplyDock")
        self._process = None
        self._generated_mesh = None
        self._source_mesh = None
        self._selected_profile = None
        self._selected_lighting_preset = None
        self._retail_queue = []
        self._retail_outputs = []
        self._retail_bindings = {}
        self._retail_current = None
        self._environment_output = None
        self._environment_recipe = None
        self._retail_environment_url = None
        self._captured_environment_parameters = None
        self._environment_digests = {}
        self._phase = ""
        self._elapsed = QtCore.QElapsedTimer()
        self._status_timer = QtCore.QTimer(self)
        self._status_timer.setInterval(1000)
        self._status_timer.timeout.connect(self._update_generation_status)

        self.character_combo = QtWidgets.QComboBox(self)
        self.character_combo.setObjectName("DeadlimitCharacter")
        for profile in _profiles():
            self.character_combo.addItem(profile["displayName"], profile)
        self.character_combo.currentIndexChanged.connect(self._update_preview_button)

        self.lighting_preset_combo = QtWidgets.QComboBox(self)
        self.lighting_preset_combo.setObjectName("DeadlimitLightingPreset")
        for preset in _lighting_presets():
            self.lighting_preset_combo.addItem(preset["name"], preset)

        self.environment_combo = QtWidgets.QComboBox(self)
        self.environment_combo.setObjectName("DeadlimitEnvironment")
        self.environment_combo.addItem("None", None)
        for label, source in _environment_options():
            self.environment_combo.addItem(label, source)
        self.environment_rotation = QtWidgets.QDoubleSpinBox(self)
        self.environment_rotation.setObjectName("DeadlimitEnvironmentRotation")
        self.environment_rotation.setRange(0.0, 360.0)
        self.environment_rotation.setSuffix("°")
        self.environment_rotation.setSingleStep(5.0)
        self.environment_strength = QtWidgets.QDoubleSpinBox(self)
        self.environment_strength.setObjectName("DeadlimitEnvironmentStrength")
        self.environment_strength.setRange(0.0, 2.0)
        self.environment_strength.setSingleStep(0.05)
        self.environment_strength.setDecimals(2)
        self.environment_strength.setValue(1.0)
        self.lighting_preset_combo.currentIndexChanged.connect(
            self._lighting_preset_changed)
        self.environment_combo.currentIndexChanged.connect(
            self._selected_environment_changed)
        self.environment_rotation.valueChanged.connect(self._environment_changed)
        self.environment_strength.valueChanged.connect(self._environment_changed)

        self.preview_combo = QtWidgets.QComboBox(self)
        self.preview_combo.setObjectName("DeadlimitPreviewView")
        for label, value in (
                ("Shaded", 0),
                ("Base Color", 1),
                ("Roughness", 2),
                ("Metallic", 3),
                ("Ambient Occlusion", 4),
                ("Direct Diffuse", 10),
                ("Direct Specular", 12),
                ("Rim Contribution", 13),
                ("NPR Lighting Composite", 14),
                ("Painter PBR Baseline", 15),
                ("Retail Rim Mask", 16),
                ("NPR Bounce", 17),
                ("Retail NPR Transmissive", 18),
                ("Environment Specular Raw", 19),
                ("Environment Specular Final", 20),
                ("Metal Diffuse Color", 21),
                ("NPR Specular Material Tint", 22),
                ("Retail Environment F0", 23)):
            self.preview_combo.addItem(label, value)
        self.preview_combo.currentIndexChanged.connect(self._set_preview_view)

        self.lighting_input_combo = QtWidgets.QComboBox(self)
        self.lighting_input_combo.setObjectName("DeadlimitLightingInputs")
        self.lighting_input_combo.addItem("Material / Retail", 0)
        self.lighting_input_combo.addItem("Diagnostic Neutral", 1)
        self.lighting_input_combo.currentIndexChanged.connect(self._set_preview_view)

        self.instructions_label = QtWidgets.QLabel(
            "Open a textured FBX, GLB or glTF project, choose a character, then "
            "click Preview as Deadlock. Source meshes and retail files stay unchanged.",
            self)
        self.instructions_label.setObjectName("DeadlimitQuickStart")
        self.instructions_label.setWordWrap(True)

        self.apply_button = QtWidgets.QPushButton("Preview as Deadlock", self)
        self.apply_button.setObjectName("ApplyDeadlimit")
        self.apply_button.setMinimumHeight(36)
        self.apply_button.clicked.connect(self.apply_deadlimit)
        self.rim_reset_button = QtWidgets.QPushButton(
            "Reset to Character Preset", self)
        self.rim_reset_button.setObjectName("DeadlimitRimReset")
        self.rim_reset_button.clicked.connect(self._reset_rim_to_character_preset)
        self.rim_mask_button = QtWidgets.QPushButton(
            "Create Paintable Rim Mask", self)
        self.rim_mask_button.setObjectName("DeadlimitRimMaskCreate")
        self.rim_mask_button.clicked.connect(self._create_rim_mask_channels)
        self.rim_export_button = QtWidgets.QPushButton(
            "Export Rim Mask PNGs…", self)
        self.rim_export_button.setObjectName("DeadlimitRimMaskExport")
        self.rim_export_button.clicked.connect(self._export_rim_masks)
        self.artistic_ao_button = QtWidgets.QPushButton(
            "Create Artistic AO", self)
        self.artistic_ao_button.setObjectName("DeadlimitArtisticAOCreate")
        self.artistic_ao_button.clicked.connect(self._create_artistic_ao_channels)
        self.artistic_ao_export_button = QtWidgets.QPushButton(
            "Export Artistic AO PNGs…", self)
        self.artistic_ao_export_button.setObjectName("DeadlimitArtisticAOExport")
        self.artistic_ao_export_button.clicked.connect(self._export_artistic_ao)
        self.progress = QtWidgets.QProgressBar(self)
        self.progress.setRange(0, 0)
        self.progress.setVisible(False)
        self.progress.setTextVisible(False)
        self.status_label = QtWidgets.QLabel(
            "Ready — one click builds a disposable Deadlock preview.", self)
        self.status_label.setObjectName("DeadlimitApplyStatus")
        self.status_label.setWordWrap(True)

        form = QtWidgets.QFormLayout()
        form.addRow("Character", self.character_combo)
        form.addRow("Lighting Preset", self.lighting_preset_combo)
        form.addRow("Lighting Inputs", self.lighting_input_combo)
        form.addRow("Deadlimit View", self.preview_combo)
        environment_group = QtWidgets.QGroupBox("Deadlimit Environment", self)
        environment_form = QtWidgets.QFormLayout(environment_group)
        environment_form.addRow("Environment", self.environment_combo)
        environment_form.addRow("Rotation", self.environment_rotation)
        environment_form.addRow("Strength", self.environment_strength)
        rim_group = QtWidgets.QGroupBox("Deadlimit Rim Light", self)
        rim_layout = QtWidgets.QVBoxLayout(rim_group)
        rim_help = QtWidgets.QLabel(
            "Edit Enable, Strength, Cutoff / Width, Sharpness and Up Ramp in "
            "Shader Settings. User0 is the paintable Deadlimit Rim Mask.", rim_group)
        rim_help.setWordWrap(True)
        rim_layout.addWidget(rim_help)
        rim_layout.addWidget(self.rim_reset_button)
        rim_layout.addWidget(self.rim_mask_button)
        rim_layout.addWidget(self.rim_export_button)
        artistic_ao_group = QtWidgets.QGroupBox("Deadlimit Artistic AO", self)
        artistic_ao_layout = QtWidgets.QVBoxLayout(artistic_ao_group)
        artistic_ao_help = QtWidgets.QLabel(
            "User1 is a paintable linear AO channel. It overrides retail and "
            "Painter AO only in the recovered Deadlock AO consumers.",
            artistic_ao_group)
        artistic_ao_help.setWordWrap(True)
        artistic_ao_layout.addWidget(artistic_ao_help)
        artistic_ao_layout.addWidget(self.artistic_ao_button)
        artistic_ao_layout.addWidget(self.artistic_ao_export_button)
        layout = QtWidgets.QVBoxLayout(self)
        layout.addWidget(self.instructions_label)
        layout.addLayout(form)
        layout.addWidget(self.apply_button)
        layout.addWidget(environment_group)
        layout.addWidget(rim_group)
        layout.addWidget(artistic_ao_group)
        layout.addWidget(self.progress)
        layout.addWidget(self.status_label)
        layout.addStretch(1)
        self._update_preview_button()
        self._lighting_preset_changed()

    def _update_preview_button(self, _index=None):
        character = self.character_combo.currentText() or "character"
        self.apply_button.setText("Preview {} as Deadlock".format(character))

    def _lighting_preset_changed(self, _index=None):
        preset = self.lighting_preset_combo.currentData() or {}
        environment = preset.get("environment") or {}
        source = environment.get("sourceImage")
        index = self.environment_combo.findData(source)
        self.environment_combo.blockSignals(True)
        self.environment_rotation.blockSignals(True)
        self.environment_strength.blockSignals(True)
        self.environment_combo.setCurrentIndex(index if index >= 0 else 0)
        self.environment_rotation.setValue(float(environment.get("rotationDegrees", 0.0)))
        self.environment_strength.setValue(float(environment.get("brightnessScale", 1.0)))
        self.environment_combo.blockSignals(False)
        self.environment_rotation.blockSignals(False)
        self.environment_strength.blockSignals(False)
        self._environment_changed()

    def _environment_changed(self, _value=None):
        if not substance_painter.project.is_open() or self._process is not None:
            return
        try:
            parameters = self._selected_environment_parameters()
            self._bind_captured_environment(parameters)
        except Exception as exc:
            self.status_label.setText("Environment update failed: {}".format(exc))

    def _selected_environment_changed(self, _index=None):
        name = self.environment_combo.currentText()
        preset = next((item for item in _lighting_presets()
                       if item["name"] == name), None)
        environment = (preset or {}).get("environment") or {}
        self.environment_rotation.blockSignals(True)
        self.environment_strength.blockSignals(True)
        self.environment_rotation.setValue(float(environment.get("rotationDegrees", 0.0)))
        self.environment_strength.setValue(float(environment.get("brightnessScale", 1.0)))
        self.environment_rotation.blockSignals(False)
        self.environment_strength.blockSignals(False)
        self._environment_changed()

    def _reset_rim_to_character_preset(self):
        if not substance_painter.project.is_open():
            self.status_label.setText("Open a Painter project first.")
            return
        profile = self.character_combo.currentData()
        parameters = _rim_shader_parameters(profile)
        script = r'''(function(parameters) {
          var changed = 0;
          alg.shaders.instances().forEach(function(instance) {
            if (!/^Deadlimit (Hero|Retail)/.test(instance.label)) return;
            var current = alg.shaders.parameters(instance.id);
            if (!Object.prototype.hasOwnProperty.call(current, "dl_npr_rim_strength")) return;
            alg.shaders.setParameters(instance.id, parameters);
            changed++;
          });
          return changed;
        })(PARAMETERS)'''.replace("PARAMETERS", json.dumps(parameters))
        try:
            changed = int(substance_painter.js.evaluate(script))
            if changed:
                self.status_label.setText(
                    "Rim reset to {} character preset on {} shader instance(s).".format(
                        profile["displayName"], changed))
            else:
                self.status_label.setText("Preview as Deadlock before resetting rim values.")
        except Exception as exc:
            self.status_label.setText("Rim preset reset failed: {}".format(exc))

    def _rim_mask_stacks(self):
        stacks = []
        conflicts = []
        for texture_set in substance_painter.textureset.all_texture_sets():
            if texture_set.name() == "__deadlimit_outline":
                continue
            try:
                stack = texture_set.get_stack()
            except ValueError:
                continue
            if stack.has_channel(RIM_MASK_CHANNEL):
                label = stack.get_channel(RIM_MASK_CHANNEL).label()
                if label != RIM_MASK_LABEL:
                    conflicts.append("{} ({})".format(texture_set.name(), label or "User0"))
                    continue
            stacks.append((texture_set, stack))
        if conflicts:
            raise RuntimeError(
                "User0 is already used by another channel: {}".format(
                    ", ".join(conflicts)))
        return stacks

    def _create_rim_mask_channels(self):
        if not substance_painter.project.is_open():
            self.status_label.setText("Open a Painter project first.")
            return
        try:
            stacks = self._rim_mask_stacks()
            created = 0
            for _texture_set, stack in stacks:
                if not stack.has_channel(RIM_MASK_CHANNEL):
                    stack.add_channel(
                        RIM_MASK_CHANNEL,
                        substance_painter.textureset.ChannelFormat.L8,
                        RIM_MASK_LABEL)
                    created += 1
            self.status_label.setText(
                "Deadlimit Rim Mask ready on {} Texture Set(s); {} created.".format(
                    len(stacks), created))
        except Exception as exc:
            self.status_label.setText("Rim Mask channel creation failed: {}".format(exc))

    def _export_rim_masks(self):
        if not substance_painter.project.is_open():
            self.status_label.setText("Open a Painter project first.")
            return
        try:
            stacks = [
                (texture_set, stack)
                for texture_set, stack in self._rim_mask_stacks()
                if stack.has_channel(RIM_MASK_CHANNEL)
            ]
            if not stacks:
                self.status_label.setText("Create a paintable Deadlimit Rim Mask first.")
                return
            output = QtWidgets.QFileDialog.getExistingDirectory(
                self, "Export Deadlimit Rim Mask PNGs")
            if not output:
                return
            preset_name = "Deadlimit Rim Mask PNG"
            map_name = "$textureSet_Deadlimit_Rim_Mask"
            config = {
                "exportPath": output,
                "exportShaderParams": False,
                "exportPresets": [{
                    "name": preset_name,
                    "maps": [{
                        "fileName": map_name,
                        "channels": [{
                            "destChannel": "L",
                            "srcChannel": "L",
                            "srcMapType": "documentMap",
                            "srcMapName": "user0",
                        }],
                    }],
                }],
                "exportList": [{
                    "rootPath": texture_set.name(),
                    "exportPreset": preset_name,
                } for texture_set, _stack in stacks],
                "exportParameters": [{
                    "parameters": {
                        "fileFormat": "png",
                        "bitDepth": "8",
                        "dithering": False,
                        "paddingAlgorithm": "infinite",
                    },
                }],
            }
            result = substance_painter.export.export_project_textures(config)
            exported = sum(len(paths) for paths in result.textures.values())
            self.status_label.setText(
                "Exported {} Deadlimit Rim Mask PNG(s) to {}.".format(
                    exported, output))
        except Exception as exc:
            self.status_label.setText("Rim Mask export failed: {}".format(exc))

    def _artistic_ao_stacks(self):
        stacks = []
        conflicts = []
        for texture_set in substance_painter.textureset.all_texture_sets():
            if texture_set.name() == "__deadlimit_outline":
                continue
            try:
                stack = texture_set.get_stack()
            except ValueError:
                continue
            if stack.has_channel(ARTISTIC_AO_CHANNEL):
                label = stack.get_channel(ARTISTIC_AO_CHANNEL).label()
                if label != ARTISTIC_AO_LABEL:
                    conflicts.append("{} ({})".format(
                        texture_set.name(), label or "User1"))
                    continue
            stacks.append((texture_set, stack))
        if conflicts:
            raise RuntimeError(
                "User1 is already used by another channel: {}".format(
                    ", ".join(conflicts)))
        return stacks

    def _create_artistic_ao_channels(self):
        if not substance_painter.project.is_open():
            self.status_label.setText("Open a Painter project first.")
            return
        if painter_layerstack is None:
            self.status_label.setText(
                "Automatic Artistic AO Base needs Painter 10+ (official layerstack API). "
                "Painter 9.1 cannot create or wire a Fill Layer through its supported API; "
                "no channel or paint was changed.")
            return
        try:
            stacks = self._artistic_ao_stacks()
            created = 0
            bases = 0
            for texture_set, stack in stacks:
                if not stack.has_channel(ARTISTIC_AO_CHANNEL):
                    stack.add_channel(
                        ARTISTIC_AO_CHANNEL,
                        substance_painter.textureset.ChannelFormat.L8,
                        ARTISTIC_AO_LABEL)
                    created += 1
                roots = painter_layerstack.get_root_layer_nodes(stack)
                if any(node.get_name() == ARTISTIC_AO_BASE_LABEL for node in roots):
                    continue
                position = (painter_layerstack.InsertPosition.below_node(roots[-1])
                            if roots else painter_layerstack.InsertPosition.from_textureset_stack(stack))
                fill = painter_layerstack.insert_fill(position)
                fill.set_name(ARTISTIC_AO_BASE_LABEL)
                fill.active_channels = {ARTISTIC_AO_CHANNEL}
                baked_ao = texture_set.get_mesh_map_resource(
                    substance_painter.textureset.MeshMapUsage.AO)
                if baked_ao is not None:
                    fill.set_source(ARTISTIC_AO_CHANNEL, baked_ao)
                else:
                    fill.set_source(ARTISTIC_AO_CHANNEL,
                                    painter_colormanagement.Color(1.0, 1.0, 1.0))
                bases += 1
            self._bind_artistic_ao_presence(stacks)
            self.status_label.setText(
                "Deadlimit Artistic AO ready on {} Texture Set(s); {} channels and {} "
                "baked/white base Fill Layers created.".format(len(stacks), created, bases))
        except Exception as exc:
            self.status_label.setText(
                "Artistic AO channel creation failed: {}".format(exc))

    def _bind_artistic_ao_presence(self, stacks):
        authored = [texture_set.name() for texture_set, stack in stacks
                    if stack.has_channel(ARTISTIC_AO_CHANNEL)]
        script = r'''(function(names) {
          var current = alg.shaders.shaderInstancesToObject();
          var labels = {};
          Object.keys(current.texturesets).forEach(function(name) {
            var label = current.texturesets[name].shader;
            if (!/^Deadlimit (Hero|Retail)/.test(label)) return;
            if (!labels[label]) labels[label] = [];
            labels[label].push(name);
          });
          alg.shaders.instances().forEach(function(instance) {
            var sets = labels[instance.label];
            if (!sets || !Object.prototype.hasOwnProperty.call(
                alg.shaders.parameters(instance.id), "dl_artistic_ao_present")) return;
            alg.shaders.setParameters(instance.id, {
              dl_artistic_ao_present: sets.every(function(name) {
                return names.indexOf(name) >= 0;
              })
            });
          });
        })(NAMES)'''.replace("NAMES", json.dumps(authored))
        substance_painter.js.evaluate(script)

    def _export_artistic_ao(self):
        if not substance_painter.project.is_open():
            self.status_label.setText("Open a Painter project first.")
            return
        try:
            stacks = [
                (texture_set, stack)
                for texture_set, stack in self._artistic_ao_stacks()
                if stack.has_channel(ARTISTIC_AO_CHANNEL)
            ]
            if not stacks:
                self.status_label.setText(
                    "Create a paintable Deadlimit Artistic AO channel first.")
                return
            output = QtWidgets.QFileDialog.getExistingDirectory(
                self, "Export Deadlimit Artistic AO PNGs")
            if not output:
                return
            preset_name = "Deadlimit Artistic AO PNG"
            config = {
                "exportPath": output,
                "exportShaderParams": False,
                "exportPresets": [{
                    "name": preset_name,
                    "maps": [{
                        "fileName": "$textureSet_Deadlimit_Artistic_AO",
                        "channels": [{
                            "destChannel": "L",
                            "srcChannel": "L",
                            "srcMapType": "documentMap",
                            "srcMapName": "user1",
                        }],
                    }],
                }],
                "exportList": [{
                    "rootPath": texture_set.name(),
                    "exportPreset": preset_name,
                } for texture_set, _stack in stacks],
                "exportParameters": [{
                    "parameters": {
                        "fileFormat": "png",
                        "bitDepth": "8",
                        "dithering": False,
                        "paddingAlgorithm": "infinite",
                    },
                }],
            }
            result = substance_painter.export.export_project_textures(config)
            exported = sum(len(paths) for paths in result.textures.values())
            self.status_label.setText(
                "Exported {} Deadlimit Artistic AO PNG(s) to {}.".format(
                    exported, output))
        except Exception as exc:
            self.status_label.setText(
                "Artistic AO export failed: {}".format(exc))

    def load_captured_environment(self, path):
        """Internal Default backend hook; intentionally absent from artist UI."""
        if not substance_painter.project.is_open():
            raise RuntimeError("Open a project first")
        manifest_path = Path(path).resolve()
        bundle = json.loads(manifest_path.read_text())
        if bundle.get("version") != 1 or bundle.get("source") != "reduced-csdk-event-791":
            raise RuntimeError("Unsupported capture profile")
        parameters = {"dl_captured_environment": True}
        for key, field in (("atlas", "dl_captured_atlas"), ("lut", "dl_captured_brdf")):
            texture_path = (manifest_path.parent / bundle[key]).resolve()
            if texture_path.parent != manifest_path.parent:
                raise RuntimeError("Bundle texture must be beside environment.json")
            digest = hashlib.sha256(texture_path.read_bytes()).hexdigest()[:12]
            resource = _import_or_reuse_project_texture(texture_path, "Deadlimit_Captured_{}_{}".format(key, digest))
            parameters[field] = resource.identifier().url()
        self._captured_environment_parameters = parameters
        if self.environment_combo.currentText() == "Default":
            self._bind_captured_environment(self._selected_environment_parameters())

    def _selected_environment_parameters(self):
        source = self.environment_combo.currentData()
        content_root = _csdk_content_root()
        parameters = {
            "dl_environment_available": False,
            "dl_captured_environment": False,
            "dl_environment_rotation": float(self.environment_rotation.value()),
            "dl_environment_strength": float(self.environment_strength.value()),
        }
        if source and content_root:
            path = content_root / Path(source.replace("/", os.sep))
            if path.is_file():
                if source not in self._environment_digests:
                    self._environment_digests[source] = hashlib.sha256(
                        path.read_bytes()).hexdigest()[:12]
                resource = _import_or_reuse_project_environment(
                    path, "Deadlimit_CSDK_{}_{}".format(
                        path.stem, self._environment_digests[source]))
                parameters["dl_selected_environment"] = resource.identifier().url()
                parameters["dl_environment_available"] = True
        if self.environment_combo.currentText() == "Default":
            if self._retail_environment_url:
                parameters["dl_selected_environment"] = self._retail_environment_url
                parameters["dl_environment_available"] = True
            if self._captured_environment_parameters is None:
                bundle = os.environ.get("DEADLIMIT_CAPTURED_ENVIRONMENT_BUNDLE")
                if bundle and Path(bundle).is_file():
                    self.load_captured_environment(bundle)
            if self._captured_environment_parameters:
                parameters.update(self._captured_environment_parameters)
                parameters["dl_environment_available"] = True
                # The captured atlas already contains native probe energy. The
                # Default preset's panorama brightness is the panel's neutral
                # slider value, while the captured backend needs unit gain.
                default_preset = next(item for item in _lighting_presets()
                                      if item["name"] == "Default")
                neutral = float(default_preset["environment"]["brightnessScale"])
                parameters["dl_environment_strength"] /= max(neutral, 0.0001)
        parameters["dl_environment_specular_enabled"] = parameters["dl_environment_available"]
        return parameters

    def _bind_captured_environment(self, parameters):
        if not parameters:
            return
        script = r'''(function(parameters) {
          var count = 0;
          alg.shaders.instances().forEach(function(instance) {
            if (!/^Deadlimit (Hero|Retail)/.test(instance.label)) return;
            // Painter can retain unused instances from previous shader hashes.
            if (!Object.prototype.hasOwnProperty.call(alg.shaders.parameters(instance.id),
              "dl_captured_environment")) return;
            alg.shaders.setParameters(instance.id, parameters);
            count++;
          });
          return count;
        })(PARAMETERS)'''.replace("PARAMETERS", json.dumps(parameters))
        substance_painter.js.evaluate(script)

    def _set_busy(self, busy, text):
        self.character_combo.setEnabled(not busy)
        self.lighting_preset_combo.setEnabled(not busy)
        self.environment_combo.setEnabled(not busy)
        self.environment_rotation.setEnabled(not busy)
        self.environment_strength.setEnabled(not busy)
        self.lighting_input_combo.setEnabled(not busy)
        self.preview_combo.setEnabled(not busy)
        self.apply_button.setEnabled(not busy)
        self.rim_reset_button.setEnabled(not busy)
        self.rim_mask_button.setEnabled(not busy)
        self.rim_export_button.setEnabled(not busy)
        self.artistic_ao_button.setEnabled(not busy)
        self.artistic_ao_export_button.setEnabled(not busy)
        self.progress.setVisible(busy)
        self.status_label.setText(text)

    def _restore_material_view(self):
        """Keep shader-native diagnostics visible after Painter UI callbacks."""
        application = QtWidgets.QApplication.instance()
        if application is None:
            return
        for combo in application.allWidgets():
            if not isinstance(combo, QtWidgets.QComboBox):
                continue
            material_index = combo.findText("Material", QtCore.Qt.MatchExactly)
            base_color_index = combo.findText("Base color", QtCore.Qt.MatchExactly)
            if combo.isVisible() and material_index >= 0 and base_color_index >= 0:
                combo.setCurrentIndex(material_index)

    def _set_preview_view(self, _index):
        if not substance_painter.project.is_open() or self._process is not None:
            return
        mode = int(self.preview_combo.currentData())
        input_mode = int(self.lighting_input_combo.currentData())
        try:
            # Painter 9.1 channel-solo views bypass custom shader samplers. Keep
            # the viewport in Material mode and use Deadlimit's shader-native
            # diagnostics so retail inputs remain visible.
            self._restore_material_view()
            # Painter may finish its own channel-view callback after this
            # handler. Repeat on the next event-loop turns so a Deadlimit View
            # view selected from Base color deterministically lands in Material.
            QtCore.QTimer.singleShot(0, self._restore_material_view)
            QtCore.QTimer.singleShot(100, self._restore_material_view)
            script = r"""
(function() {
  var mode = VIEW_MODE;
  var inputMode = INPUT_MODE;
  var changed = 0;
  alg.shaders.instances().forEach(function(instance) {
    if (instance.label === "Deadlimit Hero" || instance.label.indexOf("Deadlimit Retail ") === 0) {
      var parameters = alg.shaders.parameters(instance.id);
      if (!Object.prototype.hasOwnProperty.call(parameters, "dl_lighting_input_mode")) {
        return;
      }
      alg.shaders.setParameters(instance.id, {
        dl_debug_view: mode,
        dl_lighting_input_mode: inputMode
      });
      changed += 1;
    }
  });
  return changed;
})()
""".replace("VIEW_MODE", str(mode)).replace("INPUT_MODE", str(input_mode))
            changed = substance_painter.js.evaluate(script)
            if int(changed) > 0:
                self.status_label.setText("Deadlimit View: {}".format(self.preview_combo.currentText()))
        except Exception as exc:
            self.status_label.setText("Deadlimit View failed: {}".format(exc))

    def _cache_path(self, source_mesh, profile):
        digest = hashlib.sha256()
        source_stat = source_mesh.stat()
        digest.update(str(source_mesh.resolve()).encode("utf-8"))
        digest.update(str(source_stat.st_size).encode("ascii"))
        digest.update(str(source_stat.st_mtime_ns).encode("ascii"))
        digest.update(json.dumps(profile, sort_keys=True).encode("utf-8"))
        digest.update(_converter_path().read_bytes())
        vertex_color_dmx = self._vertex_color_dmx(source_mesh, profile)
        if vertex_color_dmx:
            dmx_stat = vertex_color_dmx.stat()
            digest.update(str(vertex_color_dmx.resolve()).encode("utf-8"))
            digest.update(str(dmx_stat.st_size).encode("ascii"))
            digest.update(str(dmx_stat.st_mtime_ns).encode("ascii"))
        cache_dir = Path(tempfile.gettempdir()) / "deadlimit-shade-preview-cache"
        cache_dir.mkdir(parents=True, exist_ok=True)
        return cache_dir / "{}-{}.fbx".format(profile["key"], digest.hexdigest()[:16])

    def _vertex_color_dmx(self, source_mesh, profile):
        recipe = profile.get("painterApply", {}).get("retailPreview", {})
        relative = recipe.get("vertexColorSource")
        if not relative:
            return None
        context = _project_context(source_mesh)
        if not context:
            raise RuntimeError(
                "The character profile needs extracted DMX vertex colors. Open this mesh from a Deadlimit project.")
        path = context["sourceRoot"] / Path(relative.replace("/", os.sep))
        if not path.is_file():
            raise RuntimeError(
                "Extract hero sources in Deadlimit first; vertex-color source is missing: {}".format(path))
        return path

    def _update_generation_status(self):
        seconds = self._elapsed.elapsed() / 1000.0
        self.status_label.setText(
            "{} · {:.1f} s\n"
            "Local offline processing only. Deadlock is never launched.".format(self._phase, seconds)
        )

    def _manifest_path(self, generated_mesh):
        return generated_mesh.with_suffix(generated_mesh.suffix + ".deadlimit.json")

    def _remember_source_mesh(self, generated_mesh, source_mesh, profile):
        self._manifest_path(generated_mesh).write_text(json.dumps({
            "sourceMesh": str(source_mesh.resolve()),
            "profile": profile["key"],
        }, indent=2), encoding="utf-8")

    def _recover_source_mesh(self, generated_mesh):
        manifest = self._manifest_path(generated_mesh)
        if not manifest.is_file():
            return None
        source = Path(json.loads(manifest.read_text(encoding="utf-8"))["sourceMesh"])
        return source if source.is_file() else None

    def apply_deadlimit(self):
        if self._process is not None:
            return
        if not substance_painter.project.is_open():
            self.status_label.setText("Open a Painter project first.")
            return

        self.preview_combo.blockSignals(True)
        self.preview_combo.setCurrentIndex(0)
        self.preview_combo.blockSignals(False)
        self.lighting_input_combo.blockSignals(True)
        self.lighting_input_combo.setCurrentIndex(0)
        self.lighting_input_combo.blockSignals(False)

        current_mesh = Path(substance_painter.project.last_imported_mesh_path())
        if self._source_mesh is None:
            if current_mesh.parent.name == "deadlimit-shade-preview-cache":
                self._source_mesh = self._recover_source_mesh(current_mesh)
                if self._source_mesh is None:
                    self.status_label.setText("This older preview has no source-mesh record. Reload the original mesh once.")
                    return
            else:
                self._source_mesh = current_mesh
        source_mesh = self._source_mesh
        if source_mesh.suffix.lower() not in (".fbx", ".glb", ".gltf"):
            self.status_label.setText("Deadlock preview requires an FBX, GLB or glTF project mesh.")
            return

        profile = self.character_combo.currentData()
        lighting_preset = self.lighting_preset_combo.currentData()
        try:
            converter = _converter_path()
            vertex_color_dmx = self._vertex_color_dmx(source_mesh, profile)
            output = self._cache_path(source_mesh, profile).with_suffix(source_mesh.suffix.lower())
        except Exception as exc:
            self.status_label.setText(str(exc))
            return

        self._generated_mesh = output
        self._selected_profile = profile
        self._selected_lighting_preset = lighting_preset
        self._remember_source_mesh(output, source_mesh, profile)
        self._elapsed.start()
        self._status_timer.start()
        if output.is_file():
            self._phase = "Step 2/3 · Reloading cached preview mesh"
            self._set_busy(True, self._phase)
            self._reload_generated_mesh()
            return

        self._process = QtCore.QProcess(self)
        self._process.setProgram(str(converter))
        self._process.setWorkingDirectory(str(converter.parent))
        arguments = [
            "--input", str(source_mesh),
            "--output", str(output),
            "--width-mm", str(profile["outline"]["widthMillimeters"]),
        ]
        if vertex_color_dmx:
            arguments.extend(["--vertex-color-dmx", str(vertex_color_dmx)])
        self._process.setArguments(arguments)
        self._process.finished.connect(self._generation_finished)
        self._process.errorOccurred.connect(self._generation_error)
        self._phase = "Step 1/3 · Preparing preview mesh"
        self._set_busy(True, "Step 1/3 · Starting offline FBX preparation...")
        self._process.start()

    def _generation_error(self, _error):
        if self._process is None:
            return
        detail = bytes(self._process.readAllStandardError()).decode("utf-8", "replace").strip()
        self._process.deleteLater()
        self._process = None
        self._status_timer.stop()
        self._set_busy(False, "Apply failed: {}".format(detail or "could not start the backend"))

    def _generation_finished(self, exit_code, _exit_status):
        process = self._process
        self._process = None
        stdout = bytes(process.readAllStandardOutput()).decode("utf-8", "replace").strip()
        stderr = bytes(process.readAllStandardError()).decode("utf-8", "replace").strip()
        process.deleteLater()
        if exit_code != 0 or not self._generated_mesh or not self._generated_mesh.is_file():
            self._status_timer.stop()
            self._set_busy(False, "Apply failed: {}".format(stderr or stdout or "mesh generation failed"))
            return

        self._reload_generated_mesh()

    def _reload_generated_mesh(self):
        self._phase = "Step 2/3 · Reloading mesh and preserving paint"
        self._set_busy(True, "Step 2/3 · Reloading Painter mesh and preserving paint strokes...")
        settings = substance_painter.project.MeshReloadingSettings(
            import_cameras=True, preserve_strokes=True)
        substance_painter.project.reload_mesh(
            str(self._generated_mesh), settings, self._mesh_reloaded)

    def _mesh_reloaded(self, status):
        status_text = str(status)
        if "success" not in status_text.lower():
            self._status_timer.stop()
            self._set_busy(False, "Painter mesh reload failed: {}".format(status_text))
            return
        QtCore.QTimer.singleShot(500, self._prepare_retail_preview)

    def _prepare_retail_preview(self):
        recipe = self._selected_profile.get("painterApply", {}).get("retailPreview")
        context = _project_context(self._source_mesh)
        self._retail_bindings = {}
        self._retail_outputs = []
        self._retail_queue = []
        self._retail_current = None
        self._environment_output = None
        self._retail_environment_url = None
        self._environment_recipe = self._selected_profile.get(
            "painterApply", {}).get("environmentPreview")
        if not recipe or not context or not context["retailVpk"].is_file():
            self._phase = "Step 3/3 · Assigning Hero and Outline shaders"
            self.status_label.setText(self._phase)
            self._apply_shaders()
            return

        try:
            converter = _retail_converter_path()
        except Exception as exc:
            self._status_timer.stop()
            self._set_busy(False, "Default retail preview unavailable: {}".format(exc))
            return

        vpk_stat = context["retailVpk"].stat()
        cache_root = Path(tempfile.gettempdir()) / "deadlimit-shade-retail-cache"
        if self._environment_recipe:
            environment_digest = hashlib.sha256()
            environment_digest.update(self._environment_recipe["texture"].encode("utf-8"))
            environment_digest.update(str(vpk_stat.st_size).encode("ascii"))
            environment_digest.update(str(vpk_stat.st_mtime_ns).encode("ascii"))
            environment_digest.update(converter.read_bytes())
            self._environment_output = cache_root / self._selected_profile["key"] / (
                "environment-" + environment_digest.hexdigest()[:16])
        for index, binding in enumerate(recipe["materialBindings"]):
            digest = hashlib.sha256()
            digest.update(binding["material"].encode("utf-8"))
            digest.update(str(vpk_stat.st_size).encode("ascii"))
            digest.update(str(vpk_stat.st_mtime_ns).encode("ascii"))
            digest.update(converter.read_bytes())
            output = cache_root / self._selected_profile["key"] / digest.hexdigest()[:16]
            self._retail_queue.append((index, binding, output, context, converter))
        self._phase = "Step 3/4 · Preparing default retail textures"
        self._run_next_retail_extract()

    def _run_next_retail_extract(self):
        if not self._retail_queue:
            self._prepare_environment_preview()
            return
        index, binding, output, context, converter = self._retail_queue.pop(0)
        self._retail_current = (index, binding, output)
        manifest = output / "manifest.json"
        if manifest.is_file():
            self._retail_outputs.append((index, binding, output))
            self._run_next_retail_extract()
            return
        output.mkdir(parents=True, exist_ok=True)
        self.status_label.setText("{} · {}/{}\nUsing project 0source, with read-only retail fallback.".format(
            self._phase, index + 1, len(self._retail_outputs) + len(self._retail_queue) + 1))
        self._process = QtCore.QProcess(self)
        self._process.setProgram(str(converter))
        self._process.setWorkingDirectory(str(converter.parent))
        arguments = [
            "--vpk", str(context["retailVpk"]),
            "--material", binding["material"],
            "--output", str(output),
        ]
        if context["sourceRoot"].is_dir():
            arguments.extend(["--source-root", str(context["sourceRoot"])])
        self._process.setArguments(arguments)
        self._process.finished.connect(self._retail_process_finished)
        self._process.errorOccurred.connect(self._generation_error)
        self._process.start()

    def _retail_process_finished(self, exit_code, exit_status):
        item = self._retail_current
        self._retail_current = None
        self._retail_extract_finished(exit_code, exit_status, item)

    def _retail_extract_finished(self, exit_code, _exit_status, item):
        process = self._process
        self._process = None
        stdout = bytes(process.readAllStandardOutput()).decode("utf-8", "replace").strip()
        stderr = bytes(process.readAllStandardError()).decode("utf-8", "replace").strip()
        process.deleteLater()
        if exit_code != 0 or not (item[2] / "manifest.json").is_file():
            self._status_timer.stop()
            self._set_busy(False, "Default retail texture extraction failed: {}".format(
                stderr or stdout or "unknown backend error"))
            return
        self._retail_outputs.append(item)
        self._run_next_retail_extract()

    def _prepare_environment_preview(self):
        if not self._environment_recipe or not self._environment_output:
            self._import_retail_resources()
            return
        manifest = self._environment_output / "texture-manifest.json"
        if manifest.is_file():
            self._import_retail_resources()
            return
        context = _project_context(self._source_mesh)
        converter = _retail_converter_path()
        self._environment_output.mkdir(parents=True, exist_ok=True)
        self._phase = "Step 4/5 · Preparing retail Default environment"
        self.status_label.setText(
            self._phase + "\nDecoding the referenced cubemap from the local read-only VPK.")
        self._process = QtCore.QProcess(self)
        self._process.setProgram(str(converter))
        self._process.setWorkingDirectory(str(converter.parent))
        self._process.setArguments([
            "--vpk", str(context["retailVpk"]),
            "--texture", self._environment_recipe["texture"],
            "--output", str(self._environment_output),
        ])
        self._process.finished.connect(self._environment_process_finished)
        self._process.errorOccurred.connect(self._generation_error)
        self._process.start()

    def _environment_process_finished(self, exit_code, _exit_status):
        process = self._process
        self._process = None
        stdout = bytes(process.readAllStandardOutput()).decode("utf-8", "replace").strip()
        stderr = bytes(process.readAllStandardError()).decode("utf-8", "replace").strip()
        process.deleteLater()
        manifest = self._environment_output / "texture-manifest.json"
        if exit_code != 0 or not manifest.is_file():
            self._status_timer.stop()
            self._set_busy(False, "Retail Default environment extraction failed: {}".format(
                stderr or stdout or "unknown backend error"))
            return
        self._import_retail_resources()

    def _import_retail_resources(self):
        try:
            if self._environment_recipe and self._environment_output:
                environment_manifest = json.loads(
                    (self._environment_output / "texture-manifest.json").read_text(encoding="utf-8"))
                if environment_manifest.get("compiledSha256") != self._environment_recipe["compiledSha256"]:
                    raise RuntimeError("retail Default environment hash does not match the Ivy profile")
                exported = environment_manifest.get("exported", [])
                if len(exported) != 1:
                    raise RuntimeError("retail Default cubemap did not produce one lat-long environment")
                environment = _import_or_reuse_project_environment(
                    self._environment_output / exported[0]["file"],
                    "Deadlimit_{}_Default_{}".format(
                        self._selected_profile["key"],
                        environment_manifest["compiledSha256"][:12]))
                self._retail_environment_url = environment.identifier().url()
            for index, binding, output in sorted(self._retail_outputs):
                manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
                urls = {}
                for texture in manifest["textures"]:
                    parameter = texture["parameter"]
                    if parameter not in (
                            "g_tColor",
                            "g_tNormalRoughness",
                            "g_tAmbientOcclusion",
                            "g_tTintMaskRimLightMask",
                            "g_tNprTransmissiveColor"):
                        continue
                    resource_name = "Deadlimit_{}_{}_{}_{}".format(
                        self._selected_profile["key"], index, output.name[:8], parameter)
                    resource = _import_or_reuse_project_texture(
                        output / texture["file"], resource_name)
                    urls[parameter] = resource.identifier().url()
                required = (
                    "g_tColor",
                    "g_tNormalRoughness",
                    "g_tAmbientOcclusion",
                    "g_tTintMaskRimLightMask",
                    "g_tNprTransmissiveColor")
                if any(name not in urls for name in required):
                    raise RuntimeError("retail material is missing a required preview input")
                self._retail_bindings[binding["textureSet"]] = {
                    "instance": "Deadlimit Retail {}".format(index + 1),
                    "vertexColorMultiply": float(manifest.get("floatParams", {}).get(
                        "g_fVertexColorStrength1", 0.0))
                        if int(manifest.get("intParams", {}).get("F_VERTEX_COLOR", 0)) else 0.0,
                    "color": urls["g_tColor"],
                    "normalRoughness": urls["g_tNormalRoughness"],
                    "ambientOcclusion": urls["g_tAmbientOcclusion"],
                    "tintRim": urls["g_tTintMaskRimLightMask"],
                    "nprTransmissive": urls["g_tNprTransmissiveColor"],
                }
            self._phase = "Step 5/5 · Assigning Hero, retail and Outline shaders"
            self.status_label.setText(self._phase)
            self._apply_shaders()
        except Exception as exc:
            self._status_timer.stop()
            self._set_busy(False, "Default retail texture import failed: {}".format(exc))

    def _apply_shaders(self):
        try:
            preset = self._selected_lighting_preset
            environment_parameters = self._selected_environment_parameters()
            environment_bound = environment_parameters["dl_environment_available"]
            lighting_parameters = _lighting_shader_parameters(preset, environment_bound)
            lighting_parameters.update(environment_parameters)
            lighting_parameters["presetName"] = preset["name"]
            shader_root = _shade_root() / "shaders"
            shader_urls = {
                "hero": _import_or_reuse_project_shader(
                    shader_root / "Deadlock_Hero.glsl", "Hero"),
                "outline": _import_or_reuse_project_shader(
                    shader_root / "Deadlock_Outline.glsl", "Outline"),
            }
            result = substance_painter.js.evaluate(_shader_assignment_script(
                self._selected_profile, self._retail_bindings, shader_urls,
                lighting_parameters, _rim_shader_parameters(self._selected_profile),
                [texture_set.name() for texture_set, stack in self._artistic_ao_stacks()
                 if stack.has_channel(ARTISTIC_AO_CHANNEL)]))
            summary = json.loads(result)
            profile = self._selected_profile
            elapsed_seconds = self._elapsed.elapsed() / 1000.0
            self._status_timer.stop()
            environment_status = "Deadlimit environment: {}".format(
                self.environment_combo.currentText()) if environment_bound else "Environment unavailable; reflections disabled"
            self._set_busy(False, "Deadlock preview active for {} / {} · {:.1f} s\n"
                                  "{}; editable rim values loaded from the character preset.".format(
                profile["displayName"], preset["name"], elapsed_seconds, environment_status))
            self.setProperty("deadlimitLastApply", json.dumps(summary))
        except Exception as exc:
            self._status_timer.stop()
            self._set_busy(False, "Shader assignment failed: {}".format(exc))


def start_plugin():
    if PLUGIN_WIDGETS:
        return PLUGIN_WIDGETS[0]
    widget = DeadlimitApplyDock()
    substance_painter.ui.add_dock_widget(widget)
    PLUGIN_WIDGETS.append(widget)
    return widget


def close_plugin():
    for widget in PLUGIN_WIDGETS:
        substance_painter.ui.delete_ui_element(widget)
    PLUGIN_WIDGETS.clear()
