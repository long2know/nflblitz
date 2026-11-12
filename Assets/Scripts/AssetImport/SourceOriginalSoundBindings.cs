using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    // Closed integration metadata generated from the pinned original ANI_CODE operands.
    public sealed class SourceOriginalSoundBindings
    {
        public const string ContentSha256 = "b118b051a4f4bb176843928ebe338ff2165a168c9319e896e2b08fbab2ff825e";
        public const string SourceVmGitBlob = "28cef0d1082f2b0c9241930fdc8886de4c8f5438";
        public const string ContactGitBlob = "68c239fa2e617988107fbebeb4dfeff5bbfe9233";
        public const string PlayerGitBlob = "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c";
        public const bool DefaultBadLanguageEnabled = true;
        private readonly Dictionary<int, SourceAudioVmBinding> byLine = new Dictionary<int, SourceAudioVmBinding>();
        private readonly Dictionary<string, SourceAudioWeightedTable> tables = new Dictionary<string, SourceAudioWeightedTable>();
        private readonly Dictionary<string, string[]> contactSites = new Dictionary<string, string[]>();
        public ReadOnlyCollection<SourceAudioVmBinding> Bindings { get; }
        private const string Tuples =
            "qbhut_knee_anim|hut_snd|86|35|0|0\n" +
            "qbhut_knee_anim|hut_snd|96|-1|0|0\n" +
            "qbhut_knee_anim|hut_snd|105|-1|0|0\n" +
            "shotgun_anim|hut_snd|164|35|0|0\n" +
            "shotgun_anim|hut_snd|173|-1|0|0\n" +
            "shotgun_anim|hut_snd|182|-1|0|0\n" +
            "qbset_anim|hut_snd|216|35|0|0\n" +
            "qbset_anim|hut_snd|226|-1|0|0\n" +
            "qbset_anim|hut_snd|235|-1|0|0\n" +
            "qbptl_anim|hut_snd|265|-2|0|0\n" +
            "qbptr_anim|hut_snd|290|-2|0|0\n" +
            "qb_slide_anim|grnd_hit_snd|832|113|0|0\n" +
            "stiff_arm2_anim|attack_snd|867|-1|0|0\n" +
            "stiff_arm2_f_anim|attack_snd|908|-1|0|0\n" +
            "t_push_anim|attack_snd|1192|-1|0|0\n" +
            "t_running_push_anim|attack_snd|1215|-1|0|0\n" +
            "t_runkic_anim|attack_snd|1267|-1|0|0\n" +
            "punt_ball_anim|attack_snd|1341|116|0|0\n" +
            "kickball_anim|attack_snd|1382|116|0|0\n" +
            "kickball2_anim|attack_snd|1430|116|0|0\n" +
            "running_plow_anim|attack_snd|1457|-1|0|0\n" +
            "m_wavqb_anim|plyr_open_sp|1499|-1|0|0\n" +
            "stiff_arm_anim|attack_snd|1558|-1|0|0\n" +
            "stiff_arm_f_anim|attack_snd|1605|-1|0|0\n" +
            "stiff_arm_nt_anim|attack_snd|1654|-1|0|0\n" +
            "r_diveoh_anim|attack_snd|1790|47|0|0\n" +
            "r_diveoh_anim|grnd_hit_snd|1814|108|0|0\n" +
            "air_swat_anim|attack_snd|1850|-1|0|0\n" +
            "get_up_c2_hurt_anim|taunt_speech|2002|29|0|0\n" +
            "get_up_c3_hurt_anim|taunt_speech|2038|28|0|0\n" +
            "get_up_c3_hurt_anim|taunt_speech|2044|30|0|0\n" +
            "get_up_b1_hurt_anim|taunt_speech|2114|28|0|0\n" +
            "get_up_b2_hurt_anim|taunt_speech|2150|29|0|0\n" +
            "t_crzdiv_anim|attack_snd|2241|47|0|0\n" +
            "t_crzdiv_anim|grnd_hit_snd|2273|-1|0|0\n" +
            "t_crzdiv_anim|grnd_hit_snd|2286|113|0|0\n" +
            "t_crzdiv_anim|attack_snd|2313|47|0|0\n" +
            "t_crzdiv_anim|grnd_hit_snd|2336|-1|0|0\n" +
            "t_crzdiv_anim|grnd_hit_snd|2349|113|0|0\n" +
            "t_dshoul_anim|get_hit_snd|2448|57|0|0\n" +
            "t_dshoul_anim|grnd_hit_snd|2461|-1|0|0\n" +
            "t_stshld_anim|attack_snd|2530|91|0|0\n" +
            "t_sprhed_anim|attack_snd|2567|47|0|0\n" +
            "t_sprhed_anim|grnd_hit_snd|2592|-1|0|0\n" +
            "elbow_drp_anim|attack_snd|2696|91|0|0\n" +
            "elbow_drp_anim|grnd_hit_snd|2727|-1|0|-1\n" +
            "butt_drp_anim|attack_snd|2842|91|0|0\n" +
            "butt_drp_anim|grnd_hit_snd|2871|-1|0|0\n" +
            "belly_flop_anim|attack_snd|2913|91|0|0\n" +
            "belly_flop_anim|grnd_hit_snd|2940|-1|0|0\n" +
            "taunt_1_anim|taunt_speech|3144|-2|0|0\n" +
            "taunt_2_anim|taunt_speech|3169|-2|0|0\n" +
            "taunt_3_anim|taunt_speech|3194|-2|0|0\n" +
            "taunt_4_anim|taunt_speech|3219|-1|0|0\n" +
            "m_laugh1_anim|taunt_speech|3307|-3|0|0\n" +
            "m_hopclp_anim|attack_snd|3409|127|0|0\n" +
            "m_hophop_anim|attack_snd|3442|127|0|0\n" +
            "m_swipe_anim|attack_snd|3478|-1|0|0\n" +
            "m_cnv_fd2_anim|get_hit_snd|3662|-1|0|0\n" +
            "m_cnv_fd2_anim|grnd_hit_snd|3673|-1|0|0\n" +
            "m_cnv_fd2_anim|grnd_hit_snd|3684|-1|0|0\n" +
            "m_cnv_fu1_anim|get_hit_snd|3708|-1|0|0\n" +
            "m_cnv_fu1_anim|grnd_hit_snd|3719|-1|0|0\n" +
            "h_stiff_arm_react_anim|get_hit_snd|3744|94|0|0\n" +
            "h_stiff_arm_react_anim|taunt_speech|3746|33|0|0\n" +
            "h_stiff_arm_react2_anim|get_hit_snd|3772|94|0|0\n" +
            "h_stiff_arm_react2_anim|taunt_speech|3774|29|0|0\n" +
            "h_stiff_arm_react2_anim|grnd_hit_snd|3785|-1|0|-1\n" +
            "h_stiff_arm_react3_anim|get_hit_snd|3808|94|0|0\n" +
            "h_stiff_arm_react3_anim|taunt_speech|3810|33|0|0\n" +
            "h_stiff_arm_react3_anim|grnd_hit_snd|3820|-1|0|-1\n" +
            "h_flailm_anim|get_hit_snd|3869|-1|48|0\n" +
            "h_flailm_carrier_anim|get_hit_snd|3909|-1|48|0\n" +
            "h_bsidef_anim|get_hit_snd|3978|-1|-1|0\n" +
            "h_bsidef_anim|tackle_spch|3991|1|-1|0\n" +
            "h_bsidef_anim|grnd_hit_snd|3994|-1|0|-1\n" +
            "h_bsider_anim|get_hit_snd|4048|-1|-1|0\n" +
            "h_bsider_anim|tackle_spch|4061|1|-1|0\n" +
            "h_bsider_anim|grnd_hit_snd|4064|-1|0|-1\n" +
            "h_bsidel_anim|get_hit_snd|4119|-1|-1|0\n" +
            "h_bsidel_anim|tackle_spch|4132|1|-1|0\n" +
            "h_bsidel_anim|grnd_hit_snd|4135|-1|0|-1\n" +
            "h_blindr_anim|get_hit_snd|4190|-1|-1|0\n" +
            "h_blindr_anim|tackle_spch|4203|0|-1|0\n" +
            "h_blindr_anim|grnd_hit_snd|4206|-1|0|-1\n" +
            "h_blindl_anim|get_hit_snd|4267|-1|-1|0\n" +
            "h_blindl_anim|tackle_spch|4280|0|-1|0\n" +
            "h_blindl_anim|grnd_hit_snd|4283|-1|0|-1\n" +
            "h_frflip_anim|get_hit_snd|4344|-1|-1|0\n" +
            "h_frflip_anim|grnd_hit_snd|4357|-1|0|-1\n" +
            "h_falnk1_anim|get_hit_snd|4421|-1|-1|0\n" +
            "h_falnk1_anim|grnd_hit_snd|4434|-1|-1|-1\n" +
            "h_falnk1_anim|tackle_spch|4448|1|-1|0\n" +
            "h_heli1_anim|get_hit_snd|4495|-1|-1|0\n" +
            "h_heli1_anim|grnd_hit_snd|4516|-1|-1|-1\n" +
            "h_heli1_anim|tackle_spch|4530|1|-1|0\n" +
            "h_heli1_anim|grnd_hit_snd|4543|-1|0|0\n" +
            "h_spin_anim|get_hit_snd|4600|-1|28|0\n" +
            "h_spin_anim|grnd_hit_snd|4616|-1|0|-1\n" +
            "h_spin_anim|grnd_hit_snd|4634|-1|0|0\n" +
            "h_spin_anim|tackle_spch|4643|1|-1|0\n" +
            "h_spinf_anim|get_hit_snd|4689|-1|28|0\n" +
            "h_spinf_anim|grnd_hit_snd|4705|-1|0|-1\n" +
            "h_spinf_anim|grnd_hit_snd|4723|-1|0|0\n" +
            "h_spinf_anim|tackle_spch|4732|1|-1|0\n" +
            "h_front4_anim|get_hit_snd|4873|-1|-1|0\n" +
            "h_front4_anim|grnd_hit_snd|4886|-1|0|-1\n" +
            "h_front4_anim|tackle_spch|4905|1|-1|0\n" +
            "h_front5_anim|get_hit_snd|5011|-1|-1|0\n" +
            "h_front5_anim|grnd_hit_snd|5022|-1|0|-1\n" +
            "h_front5_anim|grnd_hit_snd|5039|-1|0|0\n" +
            "h_front5_anim|tackle_spch|5045|0|-1|0\n" +
            "h_back_anim|get_hit_snd|5091|-1|-1|0\n" +
            "h_back_anim|grnd_hit_snd|5112|-1|0|-1\n" +
            "h_back2_anim|get_hit_snd|5156|-1|-1|0\n" +
            "h_back2_anim|grnd_hit_snd|5167|-1|0|-1\n" +
            "h_back4_anim|get_hit_snd|5209|-1|-1|0\n" +
            "h_back4_anim|grnd_hit_snd|5221|-1|0|-1\n" +
            "h_back5_anim|get_hit_snd|5396|-1|-1|0\n" +
            "h_back5_anim|grnd_hit_snd|5421|-1|0|-1\n" +
            "h_back6_anim|get_hit_snd|5454|-1|-1|0\n" +
            "h_back6_anim|grnd_hit_snd|5471|0|-1|0\n" +
            "h_back6_anim|grnd_hit_snd|5482|-1|0|-1\n" +
            "h_front1_anim|get_hit_snd|5544|-1|-1|0\n" +
            "h_front1_anim|grnd_hit_snd|5555|-1|0|-1\n" +
            "h_front1_anim|tackle_spch|5570|0|-1|0\n" +
            "h_side1_anim|get_hit_snd|5606|-1|-1|0\n" +
            "h_side1_anim|grnd_hit_snd|5617|-1|0|-1\n" +
            "h_side1_anim|grnd_hit_snd|5639|-1|0|-1\n" +
            "h_side1f_anim|get_hit_snd|5684|-1|-1|0\n" +
            "h_side1f_anim|grnd_hit_snd|5695|-1|0|-1\n" +
            "h_side1f_anim|grnd_hit_snd|5713|-1|0|-1\n" +
            "h_back1_anim|get_hit_snd|5768|-1|-1|0\n" +
            "h_back1_anim|grnd_hit_snd|5779|-1|0|-1\n" +
            "r_stmbl_anim|taunt_speech|6048|10|0|0\n" +
            "r_crzbl2_anim|attack_snd|6071|-1|0|0\n" +
            "r_histep_anim|taunt_speech|6373|-4|0|0\n" +
            "r_skip_anim|taunt_speech|6401|-4|0|0\n" +
            "r_hihand_anim|taunt_speech|6428|-4|0|0\n" +
            "r_bktau2_anim|taunt_speech|6480|-4|0|0\n" +
            "r_ezsprint_anim|taunt_speech|6508|-5|0|0\n" +
            "r_flaild_anim|get_hit_snd|6722|-1|48|0\n" +
            "r_flaild_carrier_anim|get_hit_snd|6759|-1|48|0\n" +
            "r_flailnd_anim|taunt_speech|6794|10|0|0\n" +
            "turbo_hurdle_anim|attack_snd|6902|-1|0|0\n" +
            "hurdle_anim|attack_snd|6967|64|0|0\n" +
            "turbo_spin_hurdle_anim|attack_snd|7030|-1|0|0\n" +
            "spin_hurdle_anim|attack_snd|7090|-1|0|0\n" +
            "turbo_tf_hurdle_anim|attack_snd|7151|67|0|0\n" +
            "tf_hurdle_anim|attack_snd|7211|67|0|0\n" +
            "turbo_hurdle2_anim|attack_snd|7269|67|0|0\n" +
            "hurdle2_anim|attack_snd|7327|67|0|0\n" +
            "spin_anim|attack_snd|7367|133|0|0\n" +
            "m_mscat1_anim|taunt_speech|7459|47|0|0\n" +
            "m_mscat2_anim|taunt_speech|7484|47|0|0\n" +
            "m_qbfite_anim|get_hit_snd|7538|70|0|0\n" +
            "m_qbwhat_anim|taunt_speech|7568|16|0|0\n" +
            "end_zone1_anim|taunt_speech|7853|0|0|0\n" +
            "end_zone1_anim|taunt_speech|7860|2|0|0\n" +
            "end_zone5_anim|taunt_speech|7896|18|0|0\n" +
            "end_zone6_anim|taunt_speech|7988|20|0|0\n" +
            "end_zone9_anim|attack_snd|8033|147|0|0\n" +
            "end_zone9_anim|attack_snd|8038|147|0|0\n" +
            "end_zone9_anim|attack_snd|8043|147|0|0\n" +
            "end_zone9_anim|attack_snd|8048|147|0|0\n" +
            "end_zone9_anim|attack_snd|8053|147|0|0\n" +
            "end_zone9_anim|attack_snd|8058|182|0|0\n" +
            "end_zone9_anim|attack_snd|8063|182|0|0\n" +
            "end_zone9_anim|attack_snd|8068|181|0|0\n" +
            "end_zone9_anim|attack_snd|8073|183|0|0\n" +
            "pushbl1_anim|attack_snd|8344|-1|0|0\n" +
            "pushbl2_anim|attack_snd|8374|-1|0|0\n" +
            "pushbl3_anim|attack_snd|8404|-1|0|0\n" +
            "qb_pumps_crowd_set_anim|hut_snd|8470|-1|0|0\n" +
            "qb_pumps_crowd_set_anim|hut_snd|8473|-1|0|0\n" +
            "qb_claps_set_anim|hut_snd|8493|-1|0|0\n" +
            "qb_claps_set_anim|hut_snd|8496|-1|0|0\n" +
            "qb_rubs_knee_set_anim|hut_snd|8519|-1|0|0\n" +
            "qb_rubs_knee_set_anim|hut_snd|8522|-1|0|0\n" +
            "p_waist_drop_a_anim|grnd_hit_snd|8912|108|-1|0\n" +
            "p_waist_drop_v_anim|get_hit_snd|8951|2|0|0\n" +
            "p_waist_drop_v_anim|attack_snd|8953|27|0|0\n" +
            "p_head_grab_a_anim|attack_snd|8999|19|0|0\n" +
            "p_head_grab_a_anim|grnd_hit_snd|9006|108|0|0\n" +
            "p_head_grab_v_anim|get_hit_snd|9065|2|0|0\n" +
            "p_head_grab_v_anim|attack_snd|9067|27|0|0\n" +
            "p_hip_toss_a_anim|attack_snd|9128|19|0|0\n" +
            "p_hip_toss_v_anim|get_hit_snd|9170|2|0|0\n" +
            "p_hip_toss_v_anim|attack_snd|9172|27|0|0\n" +
            "p_hip_toss_v_anim|get_hit_snd|9185|0|-1|0\n" +
            "p_hip_toss_v_anim|grnd_hit_snd|9201|108|0|0\n" +
            "p_clothes_line_v_anim|get_hit_snd|9279|-1|0|0\n" +
            "p_clothes_line_v_anim|grnd_hit_snd|9299|108|-1|0\n" +
            "p_grab_jersey_a_anim|attack_snd|9337|27|0|0\n" +
            "p_grab_jersey_v_anim|get_hit_snd|9359|-1|0|0\n" +
            "p_grab_jersey_v_anim|get_hit_snd|9402|-1|28|0\n" +
            "p_grab_jersey_v_anim|grnd_hit_snd|9418|-1|0|-1\n" +
            "p_grab_jersey_v_anim|grnd_hit_snd|9436|-1|0|0\n" +
            "p_grab_jersey_v_anim|tackle_spch|9445|1|-1|0\n" +
            "p_run_drag_v_anim|taunt_speech|9520|37|0|0\n" +
            "p_run_drag2_v_anim|taunt_speech|9554|37|0|0\n" +
            "tie1c_a_anim|get_hit_snd|9709|6|0|0\n" +
            "tie1c_v_anim|get_hit_snd|9740|10|0|0\n" +
            "tie2c_a_anim|get_hit_snd|9777|10|0|0\n" +
            "tie3c_a_anim|get_hit_snd|9836|10|0|0\n" +
            "pushlg1a_anim|attack_snd|9903|-1|0|0\n" +
            "pushlg1v_anim|get_hit_snd|9942|-1|0|0\n" +
            "p_asidea_anim|attack_snd|9984|19|0|0\n" +
            "p_asidev_anim|get_hit_snd|10018|-1|0|0\n" +
            "p_asidfa_anim|attack_snd|10050|19|0|0\n" +
            "p_asidfv_anim|get_hit_snd|10084|-1|0|0\n" +
            "p_thrw_a_anim|get_hit_snd|10505|27|0|0\n" +
            "p_thrw_v_anim|get_hit_snd|10535|-1|-1|0\n" +
            "p_thrw_v_anim|grnd_hit_snd|10548|-1|-1|-1\n" +
            "p_fslm_a_anim|attack_snd|10932|65|0|0\n" +
            "p_fslm_v_anim|get_hit_snd|10969|-1|0|0\n" +
            "p_fslm_v_anim|grnd_hit_snd|10983|-1|0|0\n" +
            "p_fslm_v_anim|grnd_hit_snd|10998|-1|0|0\n" +
            "p_dive_v_anim|get_hit_snd|11069|-1|-1|0\n" +
            "p_dive_v_anim|grnd_hit_snd|11091|-1|0|-1\n" +
            "p_ankl_v_anim|get_hit_snd|11160|-1|0|0\n" +
            "p_ankl_v_anim|grnd_hit_snd|11165|-1|0|-1\n" +
            "p_ank2_v_anim|get_hit_snd|11247|-1|0|0\n" +
            "p_ank2_v_anim|grnd_hit_snd|11255|-1|0|-1\n" +
            "p_tkdn_a_anim|get_hit_snd|11363|-1|0|0\n" +
            "p_tkdn_a_anim|grnd_hit_snd|11385|-1|0|0\n" +
            "p_tkdn_a_anim|attack_snd|11405|50|0|0\n" +
            "p_tkdn_v_anim|get_hit_snd|11447|-1|0|0\n" +
            "p_tkdn_v_anim|grnd_hit_snd|11454|0|0|-1\n" +
            "p_face_a_anim|get_hit_snd|11594|27|0|0\n" +
            "p_face_v_anim|get_hit_snd|11628|-1|-1|0\n" +
            "p_face_v_anim|grnd_hit_snd|11639|-1|0|-1\n" +
            "p_suplb_a_anim|attack_snd|11806|91|0|0\n" +
            "p_suplb_v_anim|get_hit_snd|11842|-1|0|0\n" +
            "p_suplb_v_anim|grnd_hit_snd|11854|-1|-1|-1\n" +
            "p_suplb_v_anim|grnd_hit_snd|11873|-1|0|0\n" +
            "p_spin_a_anim|attack_snd|12366|-1|0|0\n" +
            "p_spin_v_anim|get_hit_snd|12422|-1|-1|0\n" +
            "p_spin_v_anim|grnd_hit_snd|12441|-1|0|-1\n" +
            "p_csid_a_anim|attack_snd|12483|-1|0|0\n" +
            "p_csid_v_anim|get_hit_snd|12538|-1|-1|0\n" +
            "p_csid_v_anim|grnd_hit_snd|12547|-1|0|-1\n" +
            "p_btac_a_anim|grnd_hit_snd|12617|-1|0|-1\n" +
            "p_btac_v_anim|get_hit_snd|12663|-1|-1|0\n" +
            "p_ftac_v_anim|get_hit_snd|12759|-1|-1|0\n" +
            "p_ftac_v_anim|grnd_hit_snd|12768|108|0|-1\n" +
            "p_wtac_a_anim|grnd_hit_snd|12830|-1|0|-1\n" +
            "p_wtac_v_anim|get_hit_snd|12869|-1|-1|0\n" +
            "p_frol_a_anim|attack_snd|13070|-1|0|0\n" +
            "p_frol_v_anim|grnd_hit_snd|13115|-1|-1|-1\n" +
            "p_frol_v_anim|grnd_hit_snd|13125|-1|0|0\n" +
            "p_armg_a_anim|attack_snd|13174|-1|0|0\n" +
            "p_armg_a_anim|attack_snd|13179|27|0|0\n" +
            "p_armg_v_anim|get_hit_snd|13212|-1|-1|0\n" +
            "p_armg_v_anim|grnd_hit_snd|13217|-1|0|0\n" +
            "p_armg2_a_anim|attack_snd|13266|-1|0|0\n" +
            "p_armg2_a_anim|attack_snd|13271|27|0|0\n" +
            "p_armg2_v_anim|get_hit_snd|13304|-1|-1|0\n" +
            "p_armg2_v_anim|get_hit_snd|13315|28|0|0\n" +
            "p_armg2_v_anim|grnd_hit_snd|13324|-1|0|0\n" +
            "p_armg3_a_anim|attack_snd|13371|-1|0|0\n" +
            "p_armg3_a_anim|attack_snd|13376|27|0|0\n" +
            "p_armg3_v_anim|get_hit_snd|13415|-1|-1|0\n" +
            "p_armg3_v_anim|taunt_speech|13417|11|0|0\n" +
            "p_armg3_v_anim|get_hit_snd|13428|28|0|0\n" +
            "p_armg3_v_anim|grnd_hit_snd|13454|-1|0|-1\n" +
            "p_armg3_v_anim|grnd_hit_snd|13472|-1|0|0\n" +
            "p_armg3_v_anim|tackle_spch|13481|1|-1|0\n" +
            "p_tbak_a_anim|attack_snd|13514|27|0|0\n" +
            "p_tbak_v_anim|get_hit_snd|13544|-1|0|0\n" +
            "p_tbak_v_anim|grnd_hit_snd|13552|-1|0|-1\n" +
            "p_flip_a_anim|attack_snd|13747|78|0|0\n" +
            "p_flip_v_anim|get_hit_snd|13778|-1|28|0\n" +
            "p_flip_v_anim|grnd_hit_snd|13802|-1|0|0\n" +
            "c_dive_for_ball_anim|grnd_hit_snd|13875|-1|0|0\n" +
            "c_jmpint_anim|grnd_hit_snd|13918|-1|0|0\n" +
            "c_jmpint2_anim|grnd_hit_snd|13965|-1|0|0\n" +
            "c_jmpint3_anim|grnd_hit_snd|14008|-1|0|0\n" +
            "c_sidrolf_anim|grnd_hit_snd|14512|-1|0|0\n" +
            "c_roll_anim|grnd_hit_snd|14654|-1|0|0\n" +
            "c_div2h_anim|grnd_hit_snd|14908|-1|0|0\n" +
            "m_nostomp|taunt_speech|15656|-3|0|0\n" +
            "endz_08|taunt_speech|15731|0|0|0\n" +
            "endz_06|taunt_speech|15774|0|0|0\n" +
            "fall_04|get_hit_snd|15955|-1|-1|0\n" +
            "fall_04|grnd_hit_snd|15998|-1|0|-1\n" +
            "fall_04f|get_hit_snd|16028|-1|-1|0\n" +
            "fall_04f|grnd_hit_snd|16070|-1|0|-1\n" +
            "fall_10|get_hit_snd|16100|-1|-1|0\n" +
            "fall_10|grnd_hit_snd|16139|-1|0|-1\n" +
            "fall_12|get_hit_snd|16197|-1|-1|0\n" +
            "fall_12|grnd_hit_snd|16236|-1|0|-1\n" +
            "fall_18|get_hit_snd|16272|-1|-1|0\n" +
            "fall_18|grnd_hit_snd|16315|-1|0|-1\n" +
            "turbo_hurd03_anim|attack_snd|16870|67|0|0\n" +
            "hurd03_anim|attack_snd|16922|67|0|0\n" +
            "turbo_hurd04_anim|attack_snd|16973|67|0|0\n" +
            "hurd04_anim|attack_snd|17025|67|0|0\n" +
            "turbo_hurd07_anim|attack_snd|17076|67|0|0\n" +
            "hurd07_anim|attack_snd|17128|67|0|0\n" +
            "hurdflip_anim|attack_snd|17179|67|0|0\n" +
            "turbo_hurdflip_anim|attack_snd|17230|67|0|0\n" +
            "end_zone18_anim|taunt_speech|17289|0|0|0\n" +
            "end_zone19_anim|taunt_speech|17338|0|0|0\n" +
            "end_zone2_anim|taunt_speech|17372|3|0|0\n" +
            "end_zone2_anim|taunt_speech|17377|3|0|0\n" +
            "end_zone3_anim|taunt_speech|17420|5|0|0\n" +
            "end_zone8_anim|taunt_speech|17636|3|0|0\n" +
            "end_zone10_anim|taunt_speech|17675|19|0|0\n" +
            "endz_20|taunt_speech|17897|0|0|0\n" +
            "fall_13|get_hit_snd|18001|-1|-1|0\n" +
            "fall_13|grnd_hit_snd|18042|-1|0|-1\n" +
            "fall_16|get_hit_snd|18072|-1|-1|0\n" +
            "fall_16|grnd_hit_snd|18113|-1|0|-1\n";
        private const string TableRows =
            "plr_attack_snds|119|51,64,79,91,17,18,19\n" +
            "qb_sacked_sp|258|3,6,7,12,13,13,28,30,31,170,171,240\n" +
            "weak_tackle_sp|254|0,1,2,5,11,231\n" +
            "hard_tackle_sp|256|1,2,3,4,5,6,7,8,9,10,11,14,240,241,230,231,232,233\n" +
            "im_open_sp|321|168,169,170,171,172,173,174,175,176\n" +
            "disappointed_sp|248|45,46\n" +
            "hut_snds|169|35,38\n" +
            "qb_point_snds|160|38,30,31,32\n" +
            "crowd_cheers_snds|176|11,12,11\n" +
            "tackle_crowd_cheers_snds|183|11,20,12,21,12,13,20,12,11,21\n" +
            "monkey_tackle_sp|252|78,79\n" +
            "qb_duck_sp|300|246,247,248\n" +
            "hit_receiver_sp|280|92,134\n" +
            "head_plow_sp|306|72,152,153,153\n" +
            "late_hit_sp|294|140,141,142,143,144,145,146,146,147,148,149\n" +
            "no_swear_late_hit_sp|296|140,141,149,142,143,146,147,140,143,142,146,140,141,149,142,146,143,147\n" +
            "hurdle_sp|302|154,155,155,155\n" +
            "throw_ball_snds|203|158,158,158,158\n" +
            "throw_short_sp|270|32,40,50,51,52\n" +
            "throw_jump_sp|276|48,49,50,52\n" +
            "throw_med_sp|272|32,41,36,39,40,41,42,43,53\n" +
            "throw_long_sp|274|37,34,41,58,42,41,53,54,57,58,59,57,41,210,211\n" +
            "catch_fire_sp|266|182,183,71\n" +
            "catch_bobble_sp|268|77,87,88,71,72,99\n" +
            "catch_short_sp|262|70,84,85,86,87,88,89\n" +
            "catch_long_sp|264|70,71,72,73,74,82,83,87,88,89,90,91\n" +
            "fumble_sp|312|196,196,197\n" +
            "bobble_sp|310|98,98,195\n" +
            "deflected_sp|317|194,195,33,194,195\n" +
            "clock_sp|334|204,0,205,206,0\n" +
            "critical_sp|332|137,138,139\n" +
            "field_goal_sp|87|100,101\n" +
            "team_name_sp_ticks|353|40,39,41,36,54,47,51,56,56,56,47,49,45,57,43,52,52,55,46,48,48,52,53,56,77,70,59,45,64,47,57\n";
        private const string ContactRows =
            "detect.c|att_push|1235|snd_scall_bank|plyr_bnk_str,OUTTA_MY_FACE_SP,VOLUME3,127,LVL3\n" +
            "detect.c|att_push|1250|crowd_tackle_snds|\n" +
            "detect.c|att_push|1251|snd_scall_bank|plyr_bnk_str,P_HARD_HIT2_SND,VOLUME4,127,PRIORITY_LVL4\n" +
            "detect.c|att_push|1257|snd_scall_bank|plyr_bnk_str,P_HARD_HIT2_SND,VOLUME4,127,PRIORITY_LVL4\n" +
            "detect.c|att_dive|1426|crowd_cheer_snds|\n" +
            "detect.c|att_dive|1438|crowd_tackle_snds|\n" +
            "detect.c|att_dive|1444|crowd_tackle_snds|\n" +
            "detect.c|att_dive|1451|crowd_tackle_snds|\n" +
            "detect.c|att_dive|1476|monkey_on_back_sp|\n" +
            "detect.c|att_dive|1482|monkey_on_back_sp|\n" +
            "detect.c|att_dive|1501|snd_scall_bank|plyr_bnk_str,OUTTA_MY_FACE_SP,VOLUME3,127,LVL3\n" +
            "detect.c|att_dive|1518|snd_scall_bank|plyr_bnk_str,OUTTA_MY_FACE_SP,VOLUME3,127,LVL3\n" +
            "detect.c|att_dive|1520|qb_duck_speech|\n" +
            "detect.c|att_dive|1532|crowd_tackle_snds|\n" +
            "detect.c|att_dive|1596|crowd_tackle_snds|\n" +
            "detect.c|att_kick|1737|crowd_tackle_snds|\n" +
            "detect.c|att_late_hit|1770|crowd_tackle_snds|\n" +
            "detect.c|att_late_hit|1771|late_hit_speech|\n" +
            "detect.c|att_spear_head|1888|crowd_cheer_snds|\n" +
            "detect.c|att_spear_head|1911|monkey_on_back_sp|\n" +
            "detect.c|att_spear_head|1917|monkey_on_back_sp|\n" +
            "detect.c|att_spear_head|1936|snd_scall_bank|plyr_bnk_str,OUTTA_MY_FACE_SP,VOLUME3,127,LVL3\n" +
            "detect.c|att_spear_head|1953|snd_scall_bank|plyr_bnk_str,OUTTA_MY_FACE_SP,VOLUME3,127,LVL3\n" +
            "detect.c|att_spear_head|1955|qb_duck_speech|\n" +
            "detect.c|att_spear_head|1976|hit_intended_receiver_sp|\n" +
            "detect.c|att_spear_head|2017|crowd_tackle_snds|\n" +
            "detect.c|att_head|2124|snd_scall_bank|plyr_bnk_str,BOWLING_PIN_SND,VOLUME4,127,PRIORITY_LVL4\n" +
            "detect.c|att_head|2125|qcreate|plow,0,delay_plow_proc,0,0,0,0\n" +
            "detect.c|evade_vs_tackle|2297|snd_scall_bank|plyr_bnk_str,OUTTA_MY_FACE_SP,VOLUME3,127,LVL3\n" +
            "detect.c|evade_vs_tackle|2329|monkey_on_back_sp|\n" +
            "detect.c|evade_vs_tackle|2335|monkey_on_back_sp|\n" +
            "detect.c|evade_vs_tackle|2498|crowd_tackle_snds|\n" +
            "player.c|maybe_pop_helmet|9197|snd_scall_bank|ancr_swaped_bnk_str,165,ANCR_VOL2,127,LVL4\n" +
            "player.c|maybe_pop_helmet|9203|snd_scall_bank|ancr_swaped_bnk_str,166,ANCR_VOL2,127,LVL4\n" +
            "player.c|maybe_pop_helmet|9204|snd_scall_bank|plyr_bnk_str,P_HARD_HIT3_SND,ANCR_VOL2,127,PRIORITY_LVL3\n";

        private SourceOriginalSoundBindings()
        {
            RosterGuard.Require(RosterGuard.Sha256(Encoding.ASCII.GetBytes(Tuples + TableRows + ContactRows)) == ContentSha256,
                "The independently frozen original sound tuple metadata changed.");
            var bindings = new List<SourceAudioVmBinding>();
            foreach (string row in Tuples.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|');
                RosterGuard.Require(fields.Length == 6, "Invalid closed original sound tuple metadata.");
                var binding = new SourceAudioVmBinding("plyrseq.c", fields[0], fields[1],
                    int.Parse(fields[2], CultureInfo.InvariantCulture), int.Parse(fields[3], CultureInfo.InvariantCulture),
                    int.Parse(fields[4], CultureInfo.InvariantCulture), int.Parse(fields[5], CultureInfo.InvariantCulture));
                byLine.Add(binding.SourceLine, binding); bindings.Add(binding);
            }
            Bindings = bindings.AsReadOnly();
            foreach (string row in TableRows.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|'), values = fields[2].Split(',');
                var ids = new int[values.Length];
                for (int i = 0; i < ids.Length; i++) ids[i] = int.Parse(values[i], CultureInfo.InvariantCulture);
                tables.Add(fields[0], new SourceAudioWeightedTable(fields[0],
                    int.Parse(fields[1], CultureInfo.InvariantCulture), ids));
            }
            foreach (string row in ContactRows.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|');
                RosterGuard.Require(fields.Length == 5, "Invalid closed original contact sound metadata.");
                contactSites.Add(fields[0] + ":" + fields[2], fields);
            }
        }

        public void RequireContact(SourceContactSoundCue cue)
        {
            string[] fields;
            if (cue == null || !contactSites.TryGetValue(cue.Context.SourcePath + ":" +
                cue.Context.SourceLine.ToString(CultureInfo.InvariantCulture), out fields) ||
                fields[1] != cue.Context.ExecutingSymbol || fields[3] != cue.FunctionSymbol)
                throw new NotSupportedException("Unregistered original contact sound call site.");
            string[] arguments = fields[4].Length == 0 ? new string[0] : fields[4].Split(',');
            if (arguments.Length != cue.OriginalArguments.Count)
                throw new NotSupportedException("Original contact sound operand count changed.");
            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] != cue.OriginalArguments[i])
                    throw new NotSupportedException("Original contact sound operand changed.");
        }

        public SourceAudioWeightedTable Table(string symbol)
        {
            SourceAudioWeightedTable value;
            if (!tables.TryGetValue(symbol, out value))
                throw new NotSupportedException("Unregistered complete-original source sound table.");
            return value;
        }

        public static SourceOriginalSoundBindings ForCompleteOriginal303(ISourceAnimationLibrary library)
        {
            SourceTeamResourceProfile.CompleteOriginal303.RequireLibrary(library);
            return new SourceOriginalSoundBindings();
        }

        public static SourceOriginalSoundBindings ForCompleteAv306(ISourceAnimationLibrary library)
        {
            SourceTeamResourceProfile.CompleteAv306.RequireLibrary(library);
            return new SourceOriginalSoundBindings();
        }

        internal static SourceOriginalSoundBindings ForCompleteOriginalPresentation(SourceCinematicLibrary library)
        {
            bool original = library != null &&
                (library.ContentSha256 == SourceTeamResourceProfile.CompleteOriginalImpactSha256 ||
                 library.ContentSha256 == SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256) &&
                library.ContactAnimationManifestSha256 == SourceTeamResourceProfile.CompleteOriginalAnimationSha256 &&
                library.C2CallbackDataSha256 == SourceTeamResourceProfile.CompleteOriginalCallbackSha256;
            bool av = library != null && library.ContentSha256 == SourceTeamResourceProfile.CompleteAv306ImpactSha256 &&
                library.ContactAnimationManifestSha256 == SourceTeamResourceProfile.CompleteAv306AnimationSha256 &&
                library.C2CallbackDataSha256 == SourceTeamResourceProfile.CompleteAv306CallbackSha256;
            if (!original && !av)
                throw new NotSupportedException("Original hut presentation requires its exact complete-original admission.");
            return new SourceOriginalSoundBindings();
        }

        public bool TryGet(SourceCallbackCue cue, out SourceAudioVmBinding binding)
        {
            binding = null;
            if (cue == null || cue.Kind != SourcePresentationCueKind.SoundCallback ||
                !byLine.TryGetValue(cue.Context.SourceLine, out binding)) return false;
            if (binding.ScriptSymbol == cue.Context.ExecutingSymbol && binding.Matches(cue)) return true;
            binding = null; return false;
        }

        public SourceAudioVmBinding Require(SourceCallbackCue cue)
        {
            SourceAudioVmBinding binding;
            if (!TryGet(cue, out binding))
                throw new NotSupportedException("Unregistered complete-original script/function/source/argument tuple.");
            return binding;
        }
    }
}
