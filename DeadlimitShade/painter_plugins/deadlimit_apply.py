"""Deadlimit Shade panel for applying a character profile to the open project."""

import json
import hashlib
import os
import tempfile
from pathlib import Path

from PySide2 import QtCore, QtWidgets

import substance_painter.js
import substance_painter.project
import substance_painter.ui


PLUGIN_WIDGETS = []


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


def _converter_path():
    candidates = [
        _shade_root() / "tools" / "Deadlimit.MeshPreview.exe",
        _shade_root() / "tools" / "Deadlimit.MeshPreview" / "bin" / "Release" / "net8.0" / "win-x64" / "publish" / "Deadlimit.MeshPreview.exe",
        _shade_root() / "tools" / "Deadlimit.MeshPreview" / "bin" / "Release" / "net8.0" / "win-x64" / "Deadlimit.MeshPreview.exe",
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise RuntimeError("Deadlimit.MeshPreview.exe was not found in the plugin runtime")


def _shader_assignment_script(profile):
    character_id = int(profile["id"])
    hero_texture_sets = json.dumps(profile["painterApply"]["heroTextureSets"])
    return r"""
(function() {
  alg.resources.refreshShelves();
  var heroResources = alg.resources.findResources("*", "*Deadlock_Hero*");
  var outlineResources = alg.resources.findResources("*", "*Deadlock_Outline*");
  if (heroResources.length !== 1 || outlineResources.length !== 1) {
    throw new Error("Deadlimit Hero/Outline shader resources must each resolve once");
  }

  var current = alg.shaders.shaderInstancesToObject();
  var names = Object.keys(current.shaders);
  if (names.length === 0) {
    throw new Error("Painter project has no source shader instance");
  }
  var heroTextureSets = HERO_TEXTURE_SETS;
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
  hero.shader = "Deadlock_Hero";
  hero.shaderInstance = "Deadlimit Hero";
  hero.parameters = {};
  var outline = JSON.parse(JSON.stringify(source));
  outline.shader = "Deadlock_Outline";
  outline.shaderInstance = "Deadlimit Outline";
  outline.parameters = {};

  var textureSets = {};
  Object.keys(current.texturesets).forEach(function(name) {
    textureSets[name] = {
      shader: name === "__deadlimit_outline"
        ? "Deadlimit Outline"
        : (heroTextureSets.indexOf(name) >= 0 ? "Deadlimit Hero" : current.texturesets[name].shader)
    };
  });
  if (!textureSets.__deadlimit_outline || Object.keys(textureSets).length < 2) {
    throw new Error("Generated mesh is missing the __deadlimit_outline Texture Set");
  }

  var shaders = JSON.parse(JSON.stringify(current.shaders));
  shaders["Deadlimit Hero"] = hero;
  shaders["Deadlimit Outline"] = outline;
  alg.shaders.shaderInstancesFromObject({
    format: current.format,
    shaders: shaders,
    texturesets: textureSets
  });
  var instances = alg.shaders.instances();
  var heroInstance = instances.filter(function(item) {
    return item.label === "Deadlimit Hero";
  })[0];
  var outlineInstance = instances.filter(function(item) {
    return item.label === "Deadlimit Outline";
  })[0];
  if (!heroInstance || !outlineInstance) {
    throw new Error("Deadlimit shader instances were not created");
  }
  alg.shaders.setParameters(heroInstance.id, {dl_character: CHARACTER_ID, dl_debug_view: 0});
  alg.shaders.setParameters(outlineInstance.id, {
    dl_outline_character: CHARACTER_ID,
    dl_outline_use_character_color: true
  });
  return JSON.stringify({
    characterId: CHARACTER_ID,
    heroShader: heroInstance.shader,
    outlineShader: outlineInstance.shader,
    textureSets: Object.keys(textureSets).sort()
  });
})()
""".replace("CHARACTER_ID", str(character_id)).replace("HERO_TEXTURE_SETS", hero_texture_sets)


class DeadlimitApplyDock(QtWidgets.QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Deadlimit Shade")
        self.setObjectName("DeadlimitShadeApplyDock")
        self._process = None
        self._generated_mesh = None
        self._source_mesh = None
        self._selected_profile = None
        self._phase = ""
        self._elapsed = QtCore.QElapsedTimer()
        self._status_timer = QtCore.QTimer(self)
        self._status_timer.setInterval(1000)
        self._status_timer.timeout.connect(self._update_generation_status)

        self.character_combo = QtWidgets.QComboBox(self)
        self.character_combo.setObjectName("DeadlimitCharacter")
        for profile in _profiles():
            self.character_combo.addItem(profile["displayName"], profile)

        self.apply_button = QtWidgets.QPushButton("Apply Deadlimit", self)
        self.apply_button.setObjectName("ApplyDeadlimit")
        self.apply_button.setMinimumHeight(36)
        self.apply_button.clicked.connect(self.apply_deadlimit)
        self.progress = QtWidgets.QProgressBar(self)
        self.progress.setRange(0, 0)
        self.progress.setVisible(False)
        self.progress.setTextVisible(False)
        self.status_label = QtWidgets.QLabel("Ready", self)
        self.status_label.setObjectName("DeadlimitApplyStatus")
        self.status_label.setWordWrap(True)

        form = QtWidgets.QFormLayout()
        form.addRow("Character", self.character_combo)
        layout = QtWidgets.QVBoxLayout(self)
        layout.addLayout(form)
        layout.addWidget(self.apply_button)
        layout.addWidget(self.progress)
        layout.addWidget(self.status_label)
        layout.addStretch(1)

    def _set_busy(self, busy, text):
        self.character_combo.setEnabled(not busy)
        self.apply_button.setEnabled(not busy)
        self.progress.setVisible(busy)
        self.status_label.setText(text)

    def _cache_path(self, source_mesh, profile):
        digest = hashlib.sha256()
        source_stat = source_mesh.stat()
        digest.update(str(source_mesh.resolve()).encode("utf-8"))
        digest.update(str(source_stat.st_size).encode("ascii"))
        digest.update(str(source_stat.st_mtime_ns).encode("ascii"))
        digest.update(json.dumps(profile, sort_keys=True).encode("utf-8"))
        digest.update(_converter_path().read_bytes())
        cache_dir = Path(tempfile.gettempdir()) / "deadlimit-shade-preview-cache"
        cache_dir.mkdir(parents=True, exist_ok=True)
        return cache_dir / "{}-{}.fbx".format(profile["key"], digest.hexdigest()[:16])

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
            self.status_label.setText("Apply Deadlimit requires an FBX, GLB or glTF project mesh.")
            return

        profile = self.character_combo.currentData()
        output = self._cache_path(source_mesh, profile).with_suffix(source_mesh.suffix.lower())
        try:
            converter = _converter_path()
        except Exception as exc:
            self.status_label.setText(str(exc))
            return

        self._generated_mesh = output
        self._selected_profile = profile
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
        self._process.setArguments([
            "--input", str(source_mesh),
            "--output", str(output),
            "--width-mm", str(profile["outline"]["widthMillimeters"]),
        ])
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
        self._phase = "Step 3/3 · Assigning Hero and Outline shaders"
        self.status_label.setText(self._phase)
        QtCore.QTimer.singleShot(1000, self._apply_shaders)

    def _apply_shaders(self):
        try:
            result = substance_painter.js.evaluate(_shader_assignment_script(self._selected_profile))
            summary = json.loads(result)
            profile = self._selected_profile
            color = profile["outline"]["color"]
            elapsed_seconds = self._elapsed.elapsed() / 1000.0
            self._status_timer.stop()
            self._set_busy(False, "Applied {} in {:.1f} s · {:.1f} mm · RGB {:.3f}, {:.3f}, {:.3f}".format(
                profile["displayName"], elapsed_seconds,
                profile["outline"]["widthMillimeters"],
                color[0], color[1], color[2]))
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
