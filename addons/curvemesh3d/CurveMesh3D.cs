// Copyright (C) 2022 Claudio Z. (cloudofoz)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
// Rewritten in C# by Vladimir D. (stealingudc)

using System;
using System.Linq;
using Godot;

[Tool]
public partial class CurveMesh3D : Path3D {
  /*
  * CONSTANTS
  */
  const float CM_HALF_PI = Mathf.Pi / 2.0f;

  /*
  * PUBLIC VARIABLES, ACCESSORS
  */

  private float radius = 0.1f;
  private Curve radius_profile;
  private int radial_resolution = 8;
  private StandardMaterial3D material;

  // Caps
  private bool cap_start = true;
  private bool cap_end = true;
  private int cap_rings = 4;
  private float cap_uv_scale = 0.1f;
  private Vector2 cap_uv_offset = Vector2.Zero;

  // View
  private bool cm_enabled = true;
  private bool cm_debug_mode = false; 

  [ExportCategory("CurveMesh3D")]
  //  Sets the radius of the generated mesh
  [Export(PropertyHint.Range, "0.001,1.0,0.0001,or_greater")] public float Radius {
	  set {
	    radius = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return radius; }
  }

  // Use this [Curve] to modify the mesh radius
  [Export] Curve RadiusProfile {
	  set {
	    radius_profile = value;
	    if(radius_profile != null){
	  	radius_profile.Changed += CM_OnCurveChanged;
	    } 
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return radius_profile; }
  } 

  // Number of vertices of a circular section.
  // To increase the curve subdivisions you can change [Property: curve.bake_interval] instead.
  [Export(PropertyHint.Range, "4,64,1,")] int RadialResolution {
	  set {
	    radial_resolution = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
    get { return radial_resolution; }
  }

  // Material of the generated mesh surface
  [Export] StandardMaterial3D Material {
	  set {
	    material = value;
	    if(cm_mesh != null && cm_mesh.GetSurfaceCount() > 0){
	      cm_mesh.SurfaceSetMaterial(0, value);
	    }
	  }
	  get { return material; }
  }
  
  [ExportGroup("Caps", "cap_")]
  // If 'true' the generated mesh starts with an hemispherical surface
  [Export] bool CapStart {
	  set {
	    cap_start = value; 
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return cap_start; }
  }
  
  // If 'true' the generated mesh ends with an hemispherical surface
  [Export] bool CapEnd{
	  set {
	    cap_end = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return cap_end; }
  }
  
  // Number of rings that are used to create the hemispherical cap
  // note: the number of vertices of each ring depends on [radial_resolution]
  [Export(PropertyHint.Range, "1,32,1,or_greater")] int CapRings {
	  set {
	    cap_rings = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return cap_rings; }
  }

  // Scale caps UV coords by this factor
  [Export] float CapUVScale {
	  set {
	    cap_uv_scale = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return cap_uv_scale; }
  }
  
  // Shift caps UV coords by this offset 
  [Export] Vector2 CapUVOffset {
	  set {
	    cap_uv_offset = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
	  get { return cap_uv_offset; }
  }

  [ExportGroup("View", "cm_")]
  // Turn this off to disable mesh generation
  [Export] bool CMEnabled {
	  set {
	    cm_enabled = value;
	    if(!value) return; 
	    else EmitSignal(Path3D.SignalName.CurveChanged); 
	  } 
	  get { return cm_enabled; }
  }

  // If [cm_debug_mode=true] the node will draw only a run-time visibile curve
  [Export] bool CMDebugMode {
	  set {
	    cm_debug_mode = value;
	    EmitSignal(Path3D.SignalName.CurveChanged);
	  }
    get { return cm_debug_mode; }
  }
  
  /*
  * PRIVATE VARIABLES
  */

  MeshInstance3D cm_mesh_instance = null;
  ArrayMesh cm_mesh = null;
  SurfaceTool cm_st = null;

  /*
  * STATIC METHODS
  */

  // creates a mat3x4 to align a point on a plane orthogonal to the direction
  // note: geometry is firstly created on a XZ plane (normal: 0.0, 1.0, 0.0)
  static Transform3D CM_GetAlignedTransform(Vector3 from, Vector3 to, float t){
	  Vector3 up = Vector3.Up;
	  Vector3 direction = (to - from).Normalized();
	  Vector3 center = from.MoveToward(to, t);
	  Vector3 axis = direction.Cross(up).Normalized();
	  float angle = direction.AngleTo(up);
  
	  return Transform3D.Identity.Rotated(axis, angle).TranslatedLocal(-center);
  }
  
  static float CM_GetCurveLength(Vector3[] plist){
	  float d = 0.0f;
    int pcount = plist.Count();
	  for(int i = 0; i < pcount - 1; i++){
	    d += plist[i].DistanceTo(plist[i+1]);
	  }
	  return d;
  }

  /*
  * VIRTUAL METHODS
  */

  public override void _Ready(){
	  CM_ClearDuplicatedInternalChildren();
  
	  cm_st ??= new();
  
	  if(cm_mesh == null) cm_mesh = new(); 
	  else cm_mesh.ClearSurfaces();
  
	  if(cm_mesh_instance == null){
	    cm_mesh_instance = new(){ Mesh = cm_mesh };
	    cm_mesh_instance.SetMeta("__cm3d_internal__", true);
	    AddChild(cm_mesh_instance);
	  }

	  if(Curve == null || Curve.PointCount < 2) Curve = CM_CreateDefaultCurve();

	  radius_profile ??= CM_CreateDefaultRadiusProfile();
  
	  CurveChanged += CM_OnCurveChanged;
	  EmitSignal(Path3D.SignalName.CurveChanged);
  }

  /*
  * CALLBACKS
  */

  void CM_OnCurveChanged(){
	  if(!cm_enabled) return;
	  if(!cm_debug_mode) CM_BuildCurve();
	  else CM_DebugDraw();
  }

  /*
  * PRIVATE METHODS
  */

  private float CM_GetRadius(float t){
	  if(radius_profile == null || radius_profile.PointCount == 0){
	    return radius;
	  }
	  return radius * radius_profile.Sample(t);
  }

  private void CM_GenCircleVerts(Transform3D t3d, float t = 0.0f){
	  float rad_step = Mathf.Tau / radial_resolution;
	  Vector3 center = Vector3.Zero * t3d;

	  float radius = CM_GetRadius(t);
	  for(int i = 0; i < radial_resolution + 1; i++){
	    int k = i % radial_resolution;
	    float angle = k * rad_step;
	    Vector3 v = new Vector3(radius * Mathf.Cos(angle), 0.0f, radius * Mathf.Sin(angle)) * t3d;
	    cm_st.SetNormal((v - center).Normalized());
	    cm_st.SetUV(new Vector2((float) i / radial_resolution, t));
	    cm_st.AddVertex(v);
	  }
  }

	// radial_resolution +1 because: first and last vertices are in the same position 
	// BUT have 2 different UVs: v_first = uv[0.0, y_coord] | v_last = uv[1.0, y_coord] 
  private void CM_GenCurveSegment(int start_ring_idx){
	  int ring_vtx_count = radial_resolution + 1;
	  start_ring_idx *= ring_vtx_count;
	  for(int a = start_ring_idx; a < start_ring_idx + radial_resolution; a++){
	    int b = a + 1;
	    int d = a + ring_vtx_count;
	    int c = d + 1;
	    // Is there a better-looking way to write this? -V
	    cm_st.AddIndex(a);
	    cm_st.AddIndex(b);
	    cm_st.AddIndex(c);
	    cm_st.AddIndex(a);
	    cm_st.AddIndex(c);
	    cm_st.AddIndex(d);
	  }
  }

  private int CM_GenCurveSegmentsRange(int start_ring_idx, int ring_count){
	  for(int i = 0; i < ring_count; i++){
	    CM_GenCurveSegment(start_ring_idx + i);
	  }
	  return start_ring_idx + ring_count;
  }

  // parametric eq. for hemisphere on a XZ plane:
  //1. x = x0 + r * sin(beta) * cos(alpha)
  //2. y = z0 + r * cos(beta)
  //3. z = y0 + r * sin(beta) * sin(alpha)
  //4. 0 <= beta  <= HALF_PI                 # "it's a hemisphere!"
  //5. 0 <= alpha <= TAU                     # TAU = 2 * PI
  private void CM_GenCapVerts(Transform3D t3d, bool is_cap_start){
	  float alpha_step = (float) Math.Tau / cap_rings;
	  float beta_step = CM_HALF_PI / cap_rings;
	  Vector3 c = Vector3.Zero * t3d;
	  float r;
	  float beta_offset;
	  float beta_direction;

	  if(is_cap_start){
	    r = CM_GetRadius(0.0f);
	    beta_offset = CM_HALF_PI;
	    beta_direction = +1.0f;
	  } else { //is_cap_end
	    r = CM_GetRadius(1.0f);
	    beta_offset = 0.0f;
	    beta_direction = -1.0f;
	  }
	  for(int ring_idx = cap_rings; ring_idx > -1; ring_idx--){
	    float beta = beta_offset + ring_idx * beta_step * beta_direction;
	    float sin_beta = Mathf.Sin(beta);
	    float cos_beta = Mathf.Cos(beta);
	    for(int v_idx = 0; v_idx < radial_resolution + 1; v_idx++){
	  	  float alpha = v_idx % radial_resolution * alpha_step;
	  	  float sin_alpha = Mathf.Sin(alpha);
	  	  float cos_alpha = Mathf.Cos(alpha);
	  	  Vector3 v = new Vector3(r * sin_beta * cos_alpha, r * cos_beta, r * sin_beta * sin_alpha) * t3d;
	  	  cm_st.SetUV(new Vector2(
	  	     v_idx / (float) radial_resolution, 1.0f) * sin_beta * cap_uv_scale * cap_uv_offset);
	  	  cm_st.SetNormal((v - c).Normalized());
	  	  cm_st.AddVertex(v);
	    }
	  }
  }

  private int CM_GenVertices(){
	  if(Curve == null) return 0;

	  Vector3[] plist = Curve.GetBakedPoints();
	  int psize = plist.Count();

	  if(psize < 2) return 0;

	  float cur_length = 0.0f;
	  float total_length = CM_GetCurveLength(plist);

	  Transform3D t3d = CM_GetAlignedTransform(plist[0], plist[1], 0.0f);

	  if(cap_start) CM_GenCapVerts(t3d, true);
	  CM_GenCircleVerts(t3d, 0.0f);
	  for(int i = 0; i < psize - 1; i++){
	    cur_length += plist[i].DistanceTo(plist[i+1]);
	    t3d = CM_GetAlignedTransform(plist[i], plist[i+1], 1.0f);
	    CM_GenCircleVerts(t3d, Mathf.Min(cur_length / total_length, 1.0f));
	  }
	  if(cap_end) CM_GenCapVerts(t3d, false);
	  return psize;
  }

  // The whole mesh could be generated by one call, like this:
  // cm_gen_curve_segments_range(0, cap_rings * 2 + psize - 1).
  // But, at the moment, the two caps have a different uv mapping than the curve mesh.
  // For this reason caps don't share vertices with the main curve and so 
  // we need 3 separated calls of 'cm_gen_curve_segments_range()':
  // cap_start_mesh |+1| curve_mesh |+1| cap_end_mesh
  // (+1 means that we "jump" to another set of vertices).
  private void CM_GenFaces(int psize){
	  int start_idx = 0;
	  if(cap_start){
	    start_idx = CM_GenCurveSegmentsRange(0, cap_rings) + 1;
	  }
	  start_idx = CM_GenCurveSegmentsRange(start_idx, psize - 1) + 1;
	    if(cap_end){
	      _ = CM_GenCurveSegmentsRange(start_idx, cap_rings) + 1;
	    }
    }

  private bool CM_Clear(){
	if(cm_st == null || cm_mesh == null) return false;
	  cm_st.Clear();
	  cm_mesh.ClearSurfaces();
	  return true;
  }
  
  // commits the computed geometry to the mesh array
  private void CM_CurveToMeshArray(){
	  cm_st.Commit(cm_mesh);
	  cm_mesh.SurfaceSetMaterial(0, material);
  }

  private void CM_BuildCurve(){
	  if(!CM_Clear()) return;
	  cm_st.Begin(Mesh.PrimitiveType.Triangles);
	  var psize = CM_GenVertices();
	  if(psize < 2) return;
	  CM_GenFaces(psize);
	  CM_CurveToMeshArray();
  }

  private void CM_DebugDraw(){
	  if(!CM_Clear()) return;
	  cm_st.Begin(Mesh.PrimitiveType.LineStrip);
	  for(int v = 0; v < Curve.GetBakedPoints().Count(); v++){
	    cm_st.AddVertex(Curve.GetBakedPoints()[v]);
	  }
	  CM_CurveToMeshArray();
  }

  static private Curve3D CM_CreateDefaultCurve(){
	  Curve3D c = new();
	  Vector3 ctp = new(0.6f, 0.46f, 0f);
	  c.AddPoint(Vector3.Zero, ctp, ctp);
	  c.AddPoint(Vector3.Up, -ctp, -ctp);
	  c.BakeInterval = 0.1f;
	  return c;
  }

  static private StandardMaterial3D CM_CreateDefaultMaterial(){
	  StandardMaterial3D mat = new(){
	    AlbedoColor = Color.FromString("009de1", Colors.LightSkyBlue),
	    Roughness = 0.5f
	  };
	  return mat;
  }

  static private Curve CM_CreateDefaultRadiusProfile(){
	  Curve c = new();
	  c.AddPoint(new Vector2(0.0f, 0.05f));
	  // c.add_point(new Vector2(0.5f, 0.5f))
	  c.AddPoint(new Vector2(1.0f, 1.0f));
	  return c;
  }

  private void CM_ClearDuplicatedInternalChildren(){
	  for(int c = 0; c < GetChildren().Count(); c++){
	    if((bool)GetChildren()[c].GetMeta("__cm3d_internal__", false)){
	  	GetChildren()[c].QueueFree();
	    }
	  }
  }
}
