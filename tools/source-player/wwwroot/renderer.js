const identity = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

function requireValue(condition, message) {
  if (!condition) throw new Error(message);
}

function program(gl, vertex, fragment) {
  const compile = (type, source) => {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    return shader;
  };
  const result = gl.createProgram();
  const vert = compile(gl.VERTEX_SHADER, vertex), frag = compile(gl.FRAGMENT_SHADER, fragment);
  gl.attachShader(result, vert); gl.attachShader(result, frag); gl.linkProgram(result);
  gl.deleteShader(vert); gl.deleteShader(frag);
  if (!gl.getProgramParameter(result, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(result));
  return result;
}

const sceneVertex = `#version 300 es
layout(location=0) in vec3 position;
layout(location=1) in vec2 uv;
uniform mat4 viewProjection;
uniform mat4 model;
out vec2 texcoord;
void main() { texcoord=uv; gl_Position=viewProjection*model*vec4(position,1.0); }`;
const sceneFragment = `#version 300 es
precision highp float;
in vec2 texcoord;
uniform sampler2D atlas;
uniform float cutoff;
uniform bool constantAlpha;
uniform bool textureTimesAlpha;
uniform vec4 identityOutput;
out vec4 pixel;
void main() {
  vec4 color=texture(atlas,texcoord);
  if(constantAlpha) color.a=1.0;
  if(color.a<=cutoff) discard;
  if(textureTimesAlpha) color.rgb*=color.a;
  pixel=identityOutput.a>0.0?identityOutput:color;
}`;
const hudVertex = `#version 300 es
layout(location=0) in vec2 position;
layout(location=1) in vec2 uv;
layout(location=2) in vec4 color;
uniform vec2 contentSize;
out vec2 texcoord;
out vec4 vertexColor;
void main() {
  texcoord=uv; vertexColor=color;
  gl_Position=vec4(position.x/contentSize.x*2.0-1.0,1.0-position.y/contentSize.y*2.0,0.0,1.0);
}`;
const hudFragment = `#version 300 es
precision highp float;
in vec2 texcoord;
in vec4 vertexColor;
uniform sampler2D atlas;
out vec4 pixel;
void main() { pixel=texture(atlas,texcoord)*vertexColor; }`;

export class SourceRenderer {
  constructor(canvas) {
    this.canvas = canvas;
    const gl = canvas.getContext('webgl2', {
      alpha: false, antialias: true, preserveDrawingBuffer: true, powerPreference: 'high-performance',
    });
    requireValue(gl, 'Actual WebGL2 is required. No image/Canvas2D/JavaScript-gameplay fallback exists.');
    this.gl = gl;
    this.textures = new Map(); this.meshes = new Map(); this.appearances = new Map();
    this.models = new Map(); this.world = []; this.frameCount = 0; this.meshUploads = 0;
    this.scene = program(gl, sceneVertex, sceneFragment);
    this.hud = program(gl, hudVertex, hudFragment);
    this.sceneUniforms = Object.fromEntries(['viewProjection', 'model', 'atlas', 'cutoff', 'constantAlpha',
      'textureTimesAlpha', 'identityOutput'].map(name => [name, gl.getUniformLocation(this.scene, name)]));
    this.hudUniforms = Object.fromEntries(['contentSize', 'atlas'].map(name => [name, gl.getUniformLocation(this.hud, name)]));
    this.hudBuffer = gl.createBuffer();
    this.hudVao = gl.createVertexArray();
    gl.bindVertexArray(this.hudVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.hudBuffer);
    for (const [location, size, offset] of [[0, 2, 0], [1, 2, 8], [2, 4, 16]]) {
      gl.enableVertexAttribArray(location); gl.vertexAttribPointer(location, size, gl.FLOAT, false, 32, offset);
    }
    gl.bindVertexArray(null);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
    const debug = gl.getExtension('WEBGL_debug_renderer_info');
    this.device = {
      version: gl.getParameter(gl.VERSION), shadingLanguage: gl.getParameter(gl.SHADING_LANGUAGE_VERSION),
      vendor: gl.getParameter(gl.VENDOR), renderer: gl.getParameter(gl.RENDERER),
      unmaskedVendor: debug ? gl.getParameter(debug.UNMASKED_VENDOR_WEBGL) : null,
      unmaskedRenderer: debug ? gl.getParameter(debug.UNMASKED_RENDERER_WEBGL) : null,
      contextAttributes: gl.getContextAttributes(),
    };
    this.assertHealthy('WebGL initialization');
  }

  assertHealthy(stage) {
    requireValue(!this.gl.isContextLost(), `Actual WebGL context was lost at ${stage}.`);
    const error = this.gl.getError();
    requireValue(error === this.gl.NO_ERROR, `WebGL error ${error} at ${stage}.`);
  }

  texture(id, width, height, source, mipCount, repeat, linear, sha256, origin) {
    const gl = this.gl;
    requireValue(!this.textures.has(id) && width > 0 && height > 0 && /^[0-9a-f]{64}$/.test(sha256),
      `Invalid/duplicate verified texture ${id}.`);
    const data = source instanceof Uint8Array ? source : Uint8Array.from(source);
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    let offset = 0;
    for (let mip = 0; mip < mipCount; mip++) {
      const w = Math.max(1, width >> mip), h = Math.max(1, height >> mip), size = w * h * 4;
      requireValue(offset + size <= data.length, `Original mip payload is truncated: ${id}.`);
      gl.texImage2D(gl.TEXTURE_2D, mip, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, data.subarray(offset, offset + size));
      offset += size;
    }
    requireValue(offset === data.length, `Original texture byte inventory changed: ${id}.`);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_BASE_LEVEL, 0);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAX_LEVEL, mipCount - 1);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, linear ?
      mipCount > 1 ? gl.LINEAR_MIPMAP_NEAREST : gl.LINEAR : gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, linear ? gl.LINEAR : gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, repeat ? gl.REPEAT : gl.CLAMP_TO_EDGE);
    this.textures.set(id, { texture, width, height, mipCount, repeat, linear, sha256, origin });
    this.assertHealthy(`original texture ${id}`);
  }

  mesh(id, xyzuv, indices) {
    requireValue(!this.meshes.has(id) && xyzuv.length > 0 && xyzuv.length % 5 === 0 &&
      indices.length > 0 && indices.length % 3 === 0, `Invalid/duplicate original mesh ${id}.`);
    requireValue(indices.every(i => Number.isInteger(i) && i >= 0 && i < xyzuv.length / 5),
      `Original mesh has an invalid triangle index: ${id}.`);
    const gl = this.gl, vao = gl.createVertexArray();
    gl.bindVertexArray(vao);
    const vertex = gl.createBuffer(), index = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, vertex);
    gl.bufferData(gl.ARRAY_BUFFER, Float32Array.from(xyzuv), gl.STATIC_DRAW);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, index);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, Uint32Array.from(indices), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
    gl.bindVertexArray(null);
    this.meshes.set(id, { vao, vertex, index, count: indices.length, vertices: xyzuv.length / 5 });
    this.meshUploads++;
    this.assertHealthy(`original mesh ${id}`);
  }

  worldDraw(mesh, texture, state) {
    requireValue(this.meshes.has(mesh) && this.textures.has(texture), `Unbound original world draw ${mesh}.`);
    this.world.push({ mesh, texture, state: JSON.parse(state) });
  }

  model(id, joints) {
    const value = JSON.parse(joints);
    requireValue(!this.models.has(id) && id >= 0 && id < 32 && value.length === 15 &&
      value.every(j => j.parts.length > 0 && j.parts.every(p => this.meshes.has(p.mesh))),
    `Missing actual fifteen-limb original model ${id}.`);
    this.models.set(id, value);
  }

  appearance(id, metadata) {
    const value = JSON.parse(metadata);
    requireValue(!this.appearances.has(id) && value.tint === 'none' && value.textures.length >= 7 &&
      value.textures.every(t => this.textures.has(t)) && this.meshes.has(value.numberMesh) &&
      value.textureHashes.every((hash, i) => hash === this.textures.get(value.textures[i]).sha256),
    `Invalid original uniform/number bindings ${id}; no tint or alternate-team atlas substitution.`);
    this.appearances.set(id, value);
  }

  freeBall(mesh, texture, origin) {
    requireValue(this.meshes.has(mesh) && this.textures.has(texture) && !this.ball,
      'The exact original football geometry/texture is required; no sphere fallback.');
    this.ball = { mesh, texture, origin };
  }

  present(camera, joints, models, appearances, ball, hudVertices, hudColors, hudTextures, hudCounts, status) {
    const start = performance.now();
    requireValue(camera.length === 16 && joints.length === 14 * 15 * 16 && models.length === 14 &&
      appearances.length === 14 && this.world.length > 0 && this.ball, 'Incomplete actual kernel/render frame.');
    this.current = {
      camera: Float32Array.from(camera), joints: Float32Array.from(joints), models: Array.from(models),
      appearances: Array.from(appearances), ball: Array.from(ball), hudVertices: Array.from(hudVertices),
      hudColors: Array.from(hudColors), hudTextures: Array.from(hudTextures), hudCounts: Array.from(hudCounts),
    };
    this.drawScene(false);
    this.drawHud();
    this.assertHealthy('live C# source frame');
    this.frameCount++;
    this.lastMilliseconds = performance.now() - start;
    this.status = JSON.parse(status);
  }

  blend(name) {
    const gl = this.gl;
    const values = {
      GR_BLEND_ONE: gl.ONE, GR_BLEND_ZERO: gl.ZERO,
      GR_BLEND_SRC_ALPHA: gl.SRC_ALPHA, GR_BLEND_ONE_MINUS_SRC_ALPHA: gl.ONE_MINUS_SRC_ALPHA,
    };
    requireValue(Object.hasOwn(values, name), `Unsupported original blend ${name}.`);
    return values[name];
  }

  draw(meshId, textureId, matrix, idColor) {
    const gl = this.gl, mesh = this.meshes.get(meshId), texture = this.textures.get(textureId);
    requireValue(mesh && texture, `Missing original draw binding ${meshId} / ${textureId}.`);
    gl.uniformMatrix4fv(this.sceneUniforms.model, false, matrix);
    gl.uniform4f(this.sceneUniforms.identityOutput, (idColor || 0) / 255, 0, 0, idColor === null ? 0 : 1);
    gl.bindTexture(gl.TEXTURE_2D, texture.texture);
    gl.bindVertexArray(mesh.vao);
    gl.drawElements(gl.TRIANGLES, mesh.count, gl.UNSIGNED_INT, 0);
    this.drawCalls++;
  }

  drawScene(identityPass) {
    const gl = this.gl, frame = this.current;
    gl.useProgram(this.scene);
    gl.enable(gl.DEPTH_TEST); gl.depthMask(true); gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK);
    gl.frontFace(gl.CCW);
    gl.activeTexture(gl.TEXTURE0); gl.uniform1i(this.sceneUniforms.atlas, 0);
    gl.uniformMatrix4fv(this.sceneUniforms.viewProjection, false, frame.camera);
    gl.clearColor(0, 0, 0, 1); gl.clearDepth(1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    this.drawCalls = 0;
    for (const draw of this.world) {
      const state = draw.state;
      gl.depthFunc(state.depthFunction === 'GR_CMP_ALWAYS' ? gl.ALWAYS : gl.LESS);
      gl.uniform1f(this.sceneUniforms.cutoff, state.alphaFunction === 'GR_CMP_GREATER' ? state.alphaValue / 255 : -1);
      gl.uniform1i(this.sceneUniforms.constantAlpha, state.alphaSource === 'GR_ALPHASOURCE_CC_ALPHA');
      gl.uniform1i(this.sceneUniforms.textureTimesAlpha, state.colorFunction === 'GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA');
      if (identityPass) gl.disable(gl.BLEND);
      else {
        gl.enable(gl.BLEND);
        gl.blendFuncSeparate(this.blend(state.rgbSource), this.blend(state.rgbDestination),
          this.blend(state.alphaSourceBlend), this.blend(state.alphaDestinationBlend));
      }
      if (state.depthBias) { gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(0, -state.depthBias); }
      else gl.disable(gl.POLYGON_OFFSET_FILL);
      this.draw(draw.mesh, draw.texture, identity, identityPass ? 0 : null);
    }
    gl.disable(gl.POLYGON_OFFSET_FILL); gl.disable(gl.BLEND); gl.depthFunc(gl.LESS);
    gl.uniform1f(this.sceneUniforms.cutoff, 1 / 255);
    gl.uniform1i(this.sceneUniforms.constantAlpha, false);
    gl.uniform1i(this.sceneUniforms.textureTimesAlpha, false);
    for (let actor = 0; actor < 14; actor++) {
      const model = this.models.get(frame.models[actor]), appearance = this.appearances.get(frame.appearances[actor]);
      requireValue(model && appearance, `Actor ${actor} has no real original appearance/pose.`);
      for (let joint = 0; joint < 15; joint++) {
        if (!model[joint].drawEnabled) continue;
        const parts = joint === appearance.headJoint && appearance.headParts ? appearance.headParts : model[joint].parts;
        const matrix = frame.joints.subarray((actor * 15 + joint) * 16, (actor * 15 + joint + 1) * 16);
        for (const part of parts) this.draw(part.mesh, appearance.textures[part.slot], matrix, identityPass ? actor + 1 : null);
      }
      const torso = frame.joints.subarray((actor * 15 + appearance.numberMatrix) * 16,
        (actor * 15 + appearance.numberMatrix + 1) * 16);
      gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(0, -1);
      this.draw(appearance.numberMesh, appearance.numberTexture, torso, identityPass ? actor + 1 : null);
      gl.disable(gl.POLYGON_OFFSET_FILL);
    }
    if (frame.ball[0]) {
      const matrix = new Float32Array(identity);
      matrix[12] = frame.ball[1]; matrix[13] = frame.ball[2]; matrix[14] = frame.ball[3];
      this.draw(this.ball.mesh, this.ball.texture, matrix, identityPass ? 200 : null);
    }
    gl.bindVertexArray(null);
  }

  drawHud() {
    const gl = this.gl, frame = this.current;
    requireValue(frame.hudCounts.length === frame.hudColors.length && frame.hudCounts.length === frame.hudTextures.length &&
      frame.hudCounts.reduce((a, b) => a + b, 0) * 4 === frame.hudVertices.length,
    'Actual original HUD draw-command inventory is incomplete.');
    gl.useProgram(this.hud); gl.disable(gl.DEPTH_TEST); gl.depthMask(false); gl.disable(gl.CULL_FACE);
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    const rect = this.canvas.getBoundingClientRect();
    gl.uniform2f(this.hudUniforms.contentSize, rect.width, rect.height);
    gl.uniform1i(this.hudUniforms.atlas, 0);
    const vertices = new Float32Array(frame.hudVertices.length * 2);
    let first = 0;
    for (let primitive = 0; primitive < frame.hudCounts.length; primitive++) {
      const color = frame.hudColors[primitive] >>> 0;
      for (let i = 0; i < frame.hudCounts[primitive]; i++) {
        const v = first + i;
        vertices.set(frame.hudVertices.slice(v * 4, v * 4 + 4), v * 8);
        vertices[v * 8 + 4] = ((color >>> 16) & 255) / 255;
        vertices[v * 8 + 5] = ((color >>> 8) & 255) / 255;
        vertices[v * 8 + 6] = (color & 255) / 255;
        vertices[v * 8 + 7] = (color >>> 24) / 255;
      }
      first += frame.hudCounts[primitive];
    }
    gl.bindVertexArray(this.hudVao); gl.bindBuffer(gl.ARRAY_BUFFER, this.hudBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.DYNAMIC_DRAW);
    first = 0;
    for (let primitive = 0; primitive < frame.hudCounts.length;) {
      const textureId = frame.hudTextures[primitive];
      let count = 0;
      do { count += frame.hudCounts[primitive++]; } while (primitive < frame.hudCounts.length &&
        frame.hudTextures[primitive] === textureId);
      const texture = this.textures.get(textureId);
      requireValue(texture, `Actual authored HUD atlas is missing: ${textureId}.`);
      gl.bindTexture(gl.TEXTURE_2D, texture.texture); gl.drawArrays(gl.TRIANGLES, first, count);
      first += count;
    }
    gl.bindVertexArray(null); gl.depthMask(true); gl.disable(gl.BLEND);
  }

  resize() {
    const rect = this.canvas.getBoundingClientRect(), dpr = globalThis.devicePixelRatio || 1;
    const width = Math.max(1, Math.round(rect.width * dpr)), height = Math.max(1, Math.round(rect.height * dpr));
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width; this.canvas.height = height;
    }
    this.gl.viewport(0, 0, width, height);
    return { width, height, cssWidth: rect.width, cssHeight: rect.height, dpr };
  }

  proof() {
    requireValue(this.current && this.status, 'No actual live source frame exists for GPU evidence.');
    const gl = this.gl, width = 512, height = 384;
    const framebuffer = gl.createFramebuffer(), color = gl.createTexture(), depth = gl.createRenderbuffer();
    gl.bindTexture(gl.TEXTURE_2D, color);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.bindRenderbuffer(gl.RENDERBUFFER, depth); gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, width, height);
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, color, 0);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
    try {
      requireValue(gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE,
        'Actual WebGL geometry-identity proof framebuffer failed.');
      gl.viewport(0, 0, width, height);
      this.drawScene(true);
      const pixels = new Uint8Array(width * height * 4), actorPixels = Array(14).fill(0);
      let ballPixels = 0;
      gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
      for (let i = 0; i < pixels.length; i += 4) {
        if (pixels[i + 1] !== 0 || pixels[i + 2] !== 0) continue;
        if (pixels[i] >= 1 && pixels[i] <= 14) actorPixels[pixels[i] - 1]++;
        if (pixels[i] === 200) ballPixels++;
      }
      this.assertHealthy('actual original geometry GPU identity pass');
      return {
        sourceTick: this.status.sourceTick, width, height, actorPixels, ballPixels, ballOrigin: this.ball.origin,
        visibleHome: actorPixels.slice(0, 7).filter(n => n > 0).length,
        visibleAway: actorPixels.slice(7).filter(n => n > 0).length,
        method: 'Separate GPU identity framebuffer using the SAME actual original meshes, alpha textures, source poses, MatInput/projection and depth; not a substituted game screenshot.',
        device: this.device, meshUploads: this.meshUploads, worldDraws: this.world.length,
        appearances: this.current.appearances.map(id => this.appearances.get(id)),
        textureCount: this.textures.size, frameCount: this.frameCount,
      };
    } finally {
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.deleteFramebuffer(framebuffer); gl.deleteTexture(color); gl.deleteRenderbuffer(depth);
      gl.viewport(0, 0, this.canvas.width, this.canvas.height);
      this.drawScene(false); this.drawHud();
    }
  }
}
